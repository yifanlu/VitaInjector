using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using Mono.Debugger.Soft;


#if PSM_99
using NativeFunctions = VitaInjector.NativeFunctions99;
#else
using NativeFunctions = VitaInjector.NativeFunctions98;
#endif

namespace VitaInjector
{
    class MainClass
    {
        public static readonly int BLOCK_SIZE = 0x100;
        public static readonly uint MONO_IMAGES_HASHMAP_POINTER = 0x82B65674;
        public static readonly uint PSS_CODE_ALLOC_FUNC = 0x82B27695;
        public static readonly uint PSS_CODE_UNLOCK = 0x82B27669;
        public static readonly uint PSS_CODE_LOCK = 0x82B27641;

        public static void PrintHelp()
        {
            Console.WriteLine(
                "usage: VitaInjector.exe mode (file|address len out) port\n" +
                "    mode:\n" +
                "        l[oad]      launch UVLoader\n" +
                "        d[ump]      dump portion of the memory\n" +
                "    options (load only):\n" +
                "        file        homebrew ELF to launch\n" +
                "    options (dump only):\n" +
                "        address     address to start dumping\n" +
                "        len         length of dump (0 for max length)\n" +
                "        out         file to dump to\n" +
                "    options:\n" +
                "        port        Vita's COM port\n" +
                "ex:\n" +
                "    VitaInjector.exe i code.bin COM5\n" +
                "    VitaInjector.exe d 0x81000000 0x100 dump.bin COM5\n"
            );
        }

        public static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("error: arguments required.");
                PrintHelp();
                return;
            }
            switch (args[0].ToCharArray()[0])
            {
                case 'l':
                    LoadMain(args);
                    break;
                case 'd':
                    DumpMain(args);
                    break;
                case '?':
                case 'h':
                default:
                    PrintHelp();
                    break;
            }
        }

        public static void LoadMain(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("error: not enough arguments.");
                PrintHelp();
                return;
            }
            if (!File.Exists("uvloader.bin"))
            {
                Console.WriteLine("error: cannot find uvloader.bin");
                return;
            }
            if (!Directory.Exists("LoaderClient"))
            {
                Console.WriteLine("error: cannot find LoaderClient directory");
                return;
            }
            string port = args[2];
            string toload = args[1];
            string package = GetLoaderPackage(toload);
            Vita v = new Vita(port, package);
            v.Start();
            AlertClient(v);
            StartUVLoader(v);
            //Thread.Sleep (50000); // give it a few seconds
            v.Stop();
        }

        public static void DumpMain(string[] args)
        {
            if (args.Length < 5)
            {
                Console.WriteLine("error: not enough arguments.");
                PrintHelp();
                return;
            }
            string port = args[4];
            uint addr, len;
            FileStream dump;
            addr = Convert.ToUInt32(args[1], args[1].StartsWith("0x") ? 16 : 10);
            len = Convert.ToUInt32(args[2], args[2].StartsWith("0x") ? 16 : 10);
            dump = File.OpenWrite(args[3]);
            string package = GetDumpMemoryPackage();
            Vita v = new Vita(port, package);
            v.Start();
            StartDump(v, addr, len, dump);
            //Thread.Sleep (5000); // give it a few seconds
            v.Stop();
        }

        public static void AlertClient(Vita v)
        {
            Console.WriteLine("Alerting Vita of connection.");
            long connectmethod = v.GetMethod(false, "LoaderClient.AppMain", "Connect", 0, null);
            if (connectmethod < 0)
            {
                Console.WriteLine("Cannot find Connect() methond on device.");
            }
            v.RunMethod(connectmethod, null, null);
        }

        private static Int64 UIntToVitaInt(uint val)
        {
            Int64 vita_val = BitConverter.ToInt64(new byte[] { 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF }, 0);
            vita_val += val;
            return vita_val;
        }

        private static uint VitaIntToUInt(Int64 val)
        {
            Int64 vita_val = BitConverter.ToInt64(new byte[] { 0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF }, 0);
            val -= vita_val;
            return (uint)val;
        }

        public static void StartDump(Vita v, uint addr, uint len, FileStream dump)
        {
            if (len == 0)
            {
                // dump all of ram
                len = 0xFFFFFFFF - addr;
            }
            // weird address format for IntPtr on vita
            Int64 src_addr = UIntToVitaInt(addr);
            ValueImpl dest = v.GetField(false, "DumpMemory.AppMain", "dest");
            dest.Type = ElementType.Object; // must be done
            ValueImpl src = v.GetField(false, "DumpMemory.AppMain", "src");
            if (dest == null)
            {
                Console.WriteLine("Cannot find buffer to write to.");
                return;
            }
            if (src == null)
            {
                Console.WriteLine("Cannot find pointer to read from.");
                return;
            }
            long copymethod = v.GetMethod(true, "System.Runtime.InteropServices.Marshal", "Copy", 4, new string[] { "IntPtr", "Byte[]", "Int32", "Int32" });
            if (copymethod < 0)
            {
                Console.WriteLine("Cannot find Copy method.");
                return;
            }
            byte[] block = new byte[BLOCK_SIZE];
            // error block will be written when block cannot be read
            byte[] error_block = new byte[BLOCK_SIZE];
            for (int i = 0; i < BLOCK_SIZE; i++)
                error_block[i] = (byte)'X';
            ValueImpl sti = new ValueImpl();
            ValueImpl dlen = new ValueImpl();
            sti.Type = ElementType.I4;
            dlen.Type = ElementType.I4;
            sti.Value = 0;
            dlen.Value = BLOCK_SIZE;
            src.Fields[0].Value = src_addr;
            v.Suspend();
            Console.WriteLine("Starting dump...");
            for (int d = 0; d * BLOCK_SIZE <= len; d++)
            {
                try
                {
                    Console.WriteLine("Dumping 0x{0:X}", src.Fields[0].Value);
                    ValueImpl ret = v.RunMethod(copymethod, null, new ValueImpl[] { src, dest, sti, dlen }, true);
                    if (ret == null)
                    {
                        throw new TargetException("Method never returned.");
                    }
                    v.GetBuffer(dest.Objid, BLOCK_SIZE, ref block);
#if PRINT_DUMP
					PrintHexDump (block, (uint)BLOCK_SIZE, 16);
#endif
                    int num = BLOCK_SIZE;
                    if (d * BLOCK_SIZE + num > len)
                        num = (int)(len - d * BLOCK_SIZE);
                    dump.Write(block, 0, num);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error dumping 0x{0:X}: {1}", src.Fields[0].Value, ex.Message.ToString());
                    int num = BLOCK_SIZE;
                    if (d * BLOCK_SIZE + num > len)
                        num = (int)(len - d * BLOCK_SIZE);
                    dump.Write(error_block, 0, num);
                }
                // next block to dump
                src.Fields[0].Value = (Int64)src.Fields[0].Value + BLOCK_SIZE;
                if (d % 1000 == 0)
                {
                    // must be done or app will freeze
                    v.Resume();
                    v.Suspend();
                }
            }
            v.Resume();
        }

        /*
        public static void StartUVLoaderNew(Vita v)
        {
            Console.WriteLine("Loading UVL to memory.");
            long loaduvl = v.GetMethod(false, "LoaderClient.ExploitMain", "LoadPayload", 0, null);
            if (loaduvl < 0)
            {
                Console.WriteLine("Error getting method.");
                return;
            }
            ValueImpl buffer = v.RunMethod(loaduvl, null, new ValueImpl[] { });

            Console.WriteLine("Disabling security checks.");
            ValueImpl ptr = new ValueImpl();
            ptr.Type = ElementType.ValueType;
            ptr.Klass = v.GetTypeObjID(true, "System.IntPtr");
            ptr.Fields = new ValueImpl[] { new ValueImpl() };
            ptr.Fields[0].Type = ElementType.I8;
            ptr.Fields[0].Value = UIntToVitaInt(0x82B60424);
            ValueImpl zero = new ValueImpl();
            lenval.Type = ElementType.I4;
            lenval.Value = 0;
            long writeint32 = v.GetMethod(true, "System.Runtime.InteropServices.Marshal", "WriteInt32", 2, null);
            if (writeint32 < 0)
            {
                Console.WriteLine("Error getting method.");
                return;
            }
            Console.WriteLine("Nulling out first check pointer.");
            v.RunMethod(writeint32, null, new ValueImpl[] { ptr, zero });
            Console.WriteLine("Nulling out second check pointer.");
            ptr.Fields[0].Value = UIntToVitaInt(0x82B75490);
            v.RunMethod(writeint32, null, new ValueImpl[] { ptr, zero });

            // tell loader to start loading

            Console.WriteLine("Preparing to elevate privileges.");
            ValueImpl exploit_class_type = new ValueImpl();
            exploit_class_type.Type = ElementType.Object;
            exploit_class_type.Objid = v.GetTypeObjID(false, "LoaderClient.ExploitMain");
            ValueImpl exploit_delegate_type = new ValueImpl();
            exploit_delegate_type.Type = ElementType.Object;
            exploit_delegate_type.Objid = v.GetTypeObjID(true, "System.Threading.ThreadStart");
            ValueImpl run_exploit_name = new ValueImpl();
            run_exploit_name.Type = ElementType.Object;
            run_exploit_name.Objid = v.CreateString("RunExploit");
            long createdelegate = v.GetMethod(true, "System.Delegate", "CreateDelegate", 3, new string[] { "Type", "Type", "String" });
            Console.WriteLine("Elevating privileges...");
            ValueImpl runexploit = v.RunMethod(createdelegate, null, new ValueImpl[] { exploit_delegate_type, exploit_class_type, run_exploit_name });
            runexploit.Type = ElementType.Object;

            Console.WriteLine("Running first stage loader.");
            long rundelegate = v.GetMethod(true, "System.Delegate", "DynamicInvokeImpl", 1, null);
            ValueImpl nul = new ValueImpl();
            nul.Type = (ElementType)0xf0;
            v.RunMethod(rundelegate, runexploit, new ValueImpl[] { nul });

            /*
            Console.WriteLine("Creating new thread.");
            long threadctor = v.GetMethod(true, "System.Threading.Thread", ".ctor", 1, new string[] { "ThreadStart" });
            ValueImpl newthread = v.RunMethod(threadctor, null, new ValueImpl[] { runexploit });
            newthread.Type = ElementType.Object;

            Console.WriteLine("Running first stage loader.");
            long runthreadid = v.GetMethod(true, "System.Threading.Thread", "Start", 0, null);
            v.RunMethod(runthreadid, newthread, new ValueImpl[] { });
            
        }
        */

        public static long CreateArray(Vita v, string typename, ValueImpl[] items)
        {
            long type_tocreate = v.GetTypeObjID(true, typename);
            long methid_createarray = v.GetMethod(true, "System.Array", "CreateInstance", 2, new string[] { "Type", "Int32" });
            ValueImpl arg_elementtype = new ValueImpl();
            ValueImpl arg_length = new ValueImpl();
            arg_elementtype.Type = ElementType.Object;
            arg_elementtype.Objid = type_tocreate;
            arg_length.Type = ElementType.I4;
            arg_length.Value = items.Length;
            ValueImpl val_array = v.RunMethod(methid_createarray, null, new ValueImpl[] { arg_elementtype, arg_length });
            val_array.Type = ElementType.Object; // fix bug
            v.SetArray(val_array.Objid, items);
            return val_array.Objid;
        }

        public static ValueImpl CreateDelegateFromFptr(Vita v, uint fptr, string s_type_return, string[] s_type_params, ValueImpl arg_typedel)
        {
            // step 1, create the dynamic method
            long type_dynamicmethod = v.GetTypeObjID(true, "System.Reflection.Emit.DynamicMethod");
            long type_delegate = v.GetTypeObjID(true, "System.Delegate");
            long type_return = v.GetTypeObjID(true, s_type_return);
            long objid_dynmethname = v.CreateString("method_" + fptr);
            long methid_getassembly = v.GetMethod(true, "System.Reflection.Assembly", "GetAssembly", 1, new string[] { "Type"});
            ValueImpl obj_dynmaicmethodtype = new ValueImpl();
            obj_dynmaicmethodtype.Type = ElementType.Object;
            obj_dynmaicmethodtype.Objid = type_dynamicmethod;
            ValueImpl obj_callingassembly = v.RunMethod(methid_getassembly, null, new ValueImpl[] { obj_dynmaicmethodtype });
            obj_callingassembly.Type = ElementType.Object;
            long methid_getmodule = v.GetMethod(true, "System.Reflection.Assembly", "GetManifestModule", 0, null);
            ValueImpl param_dynmethconstruct_module = v.RunMethod(methid_getmodule, obj_callingassembly, null);
            param_dynmethconstruct_module.Type = ElementType.Object;

            long methid_dynmethconstruct = v.GetMethod(true, "System.Reflection.Emit.DynamicMethod", ".ctor", 4, new string[] { "String", "Type", "Type[]", "Module" });
            ValueImpl param_dynmethconstruct_name = new ValueImpl();
            ValueImpl param_dynmethconstruct_returntype = new ValueImpl();
            ValueImpl param_dynmethconstruct_paramtypes = new ValueImpl();
            ValueImpl[] arr_paramtypes_values = new ValueImpl[s_type_params.Length];
            // paramaters
            param_dynmethconstruct_name.Type = ElementType.Object;
            param_dynmethconstruct_name.Objid = objid_dynmethname;
            param_dynmethconstruct_returntype.Type = ElementType.Object;
            param_dynmethconstruct_returntype.Objid = type_return;
            param_dynmethconstruct_paramtypes.Type = ElementType.Object;
            for (int i = 0; i < s_type_params.Length; i++)
            {
                arr_paramtypes_values[i] = new ValueImpl();
                arr_paramtypes_values[i].Type = ElementType.Object;
                arr_paramtypes_values[i].Objid = v.GetTypeObjID(true, s_type_params[i]);
            }
            param_dynmethconstruct_paramtypes.Objid = CreateArray(v, "System.Type", arr_paramtypes_values);
            ValueImpl val_dynmeth = v.RunMethod(methid_dynmethconstruct, null, new ValueImpl[] { param_dynmethconstruct_name, param_dynmethconstruct_returntype, param_dynmethconstruct_paramtypes, param_dynmethconstruct_module });
            val_dynmeth.Type = ElementType.Object;

            // step 2, get the il generator
            long methid_getilgen = v.GetMethod(true, "System.Reflection.Emit.DynamicMethod", "GetILGenerator", 0, null);
            ValueImpl val_gen = v.RunMethod(methid_getilgen, val_dynmeth, null);
            val_gen.Type = ElementType.Object;

            // step 3, generate bytecode to load arguments
            long methid_emit_i32 = v.GetMethod(true, "System.Reflection.Emit.ILGenerator", "Emit", 2, new string[] { "OpCode", "Int32" });
            long methid_emit = v.GetMethod(true, "System.Reflection.Emit.ILGenerator", "Emit", 1, new string[] { "OpCode" });
            long methid_emit_calli = v.GetMethod(true, "System.Reflection.Emit.ILGenerator", "EmitCalli", 4, new string[] { "OpCode", "CallingConventions", "Type", "Type[]" });
            for (int i = 0; i < s_type_params.Length; i++)
            {
                ValueImpl opcode = v.GetField(true, "System.Reflection.Emit.OpCodes", "Ldarg");
                ValueImpl val_val = new ValueImpl();
                val_val.Type = ElementType.I4;
                val_val.Value = i;
                v.RunMethod(methid_emit_i32, val_gen, new ValueImpl[] { opcode, val_val });
            }

            // step 4, generate bytecode run function
            ValueImpl arg_opcode = v.GetField(true, "System.Reflection.Emit.OpCodes", "Ldc_I4");
            ValueImpl arg_val = new ValueImpl();
            arg_val.Type = ElementType.I4;
            arg_val.Value = (Int32)fptr;
            v.RunMethod(methid_emit_i32, val_gen, new ValueImpl[] { arg_opcode, arg_val });
            arg_opcode = v.GetField(true, "System.Reflection.Emit.OpCodes", "Conv_I");
            v.RunMethod(methid_emit, val_gen, new ValueImpl[] { arg_opcode });
            arg_opcode = v.GetField(true, "System.Reflection.Emit.OpCodes", "Calli");
            ValueImpl arg_callconv = new ValueImpl();
            arg_callconv.Type = ElementType.Enum;
            arg_callconv.Value = 3;
            v.RunMethod(methid_emit_calli, val_gen, new ValueImpl[] { arg_opcode, arg_callconv, param_dynmethconstruct_returntype, param_dynmethconstruct_paramtypes });
            arg_opcode = v.GetField(true, "System.Reflection.Emit.OpCodes", "Ret");

            // step 5, create a delegate
            long methid_createdel = v.GetMethod(true, "System.Reflection.Emit.DynamicMethod", "CreateDelegate", 1, new string[] { "Type" });
            ValueImpl del = v.RunMethod(methid_createdel, val_dynmeth, new ValueImpl[] { arg_typedel });
            del.Type = ElementType.Object;
            return del;
        }

        public static void EscalatePrivilege(Vita v)
        {
            // step 0, setup
            long methid_readintptr = v.GetMethod(true, "System.Runtime.InteropServices.Marshal", "ReadIntPtr", 2, new string[] { "IntPtr", "Int32" });
            if (methid_readintptr < 0)
            {
                throw new TargetException("Cannot get method id for ReadIntPtr");
            }
            long methid_readint32 = v.GetMethod(true, "System.Runtime.InteropServices.Marshal", "ReadInt32", 2, new string[] { "IntPtr", "Int32" });
            if (methid_readint32 < 0)
            {
                throw new TargetException("Cannot get method id for ReadInt32");
            }
            long methid_writeint32 = v.GetMethod(true, "System.Runtime.InteropServices.Marshal", "WriteInt32", 3, new string[] { "IntPtr", "Int32", "Int32" });
            if (methid_writeint32 < 0)
            {
                throw new TargetException("Cannot get method id for WriteInt32");
            }
            long methid_alloch = v.GetMethod(true, "System.Runtime.InteropServices.Marshal", "AllocHGlobal", 1, new string[] { "Int32" });
            if (methid_alloch < 0)
            {
                throw new TargetException("Cannot get method id for AllocHGlobal");
            }
            // step 1, find out where the hashmap is stored
            ValueImpl zero = new ValueImpl();
            zero.Type = ElementType.I4;
            zero.Value = 0;
            ValueImpl ptr_to_hashmap = v.RunMethod(methid_alloch, null, new ValueImpl[] { zero }); // we need the IntPtr type
            ptr_to_hashmap.Fields[0].Value = UIntToVitaInt(MONO_IMAGES_HASHMAP_POINTER);
            ValueImpl offset = new ValueImpl();
            offset.Type = ElementType.I4;
            offset.Value = 0;
            ValueImpl hashmap = v.RunMethod(methid_readintptr, null, new ValueImpl[] { ptr_to_hashmap, offset });
            Console.WriteLine("Images hashmap located at: 0x{0:X}", VitaIntToUInt((Int64)hashmap.Fields[0].Value));
            // step 2, find hashmap data
            offset.Value = 8;
            ValueImpl hashmap_data = v.RunMethod(methid_readintptr, null, new ValueImpl[] { hashmap, offset });
            Console.WriteLine("Hashmap entries located at: 0x{0:X}", VitaIntToUInt((Int64)hashmap_data.Fields[0].Value));
            offset.Value = 12;
            ValueImpl hashmap_len = v.RunMethod(methid_readint32, null, new ValueImpl[] { hashmap, offset });
            Console.WriteLine("Images hashmap has {0} entries", hashmap_len.Value);
            // step 3, get entries
            for (int i = 0; i < (Int32)hashmap_len.Value; i++)
            {
                offset.Value = i * 4;
                ValueImpl entry = v.RunMethod(methid_readintptr, null, new ValueImpl[] { hashmap_data, offset });
                while (VitaIntToUInt((Int64)entry.Fields[0].Value) > 0) // each item in slot
                {
                    Console.WriteLine("Entry {0} found at: 0x{1:X}", i, VitaIntToUInt((Int64)entry.Fields[0].Value));
                    offset.Value = 4;
                    ValueImpl image_data = v.RunMethod(methid_readintptr, null, new ValueImpl[] { entry, offset });
                    Console.WriteLine("Image data found at: 0x{0:X}", VitaIntToUInt((Int64)image_data.Fields[0].Value));
                    offset.Value = 16;
                    ValueImpl image_attributes = v.RunMethod(methid_readint32, null, new ValueImpl[] { image_data, offset });
                    Console.WriteLine("Image attributes set to: 0x{0:X}", image_attributes.Value);
                    // step 4, patch the attribute to include corlib
                    image_attributes.Value = (Int32)image_attributes.Value | (1 << 10);
                    v.RunMethod(methid_writeint32, null, new ValueImpl[] { image_data, offset, image_attributes });
                    Console.WriteLine("Image attributes patched to: 0x{0:X}", image_attributes.Value);
                    offset.Value = 8;
                    entry = v.RunMethod(methid_readintptr, null, new ValueImpl[] { entry, offset }); // next item in this slot in hashmap
                }

            }
        }

        public static void StartUVLoader(Vita v)
        {
            Console.WriteLine("Escalating privileges...");
            EscalatePrivilege(v);
            Console.WriteLine("Running first stage loader...");
            long methid_exploit = v.GetMethod(false, "LoaderClient.ExploitMain", "RunExploit", 0, null);
            v.RunMethod(methid_exploit, null, null);
            Console.WriteLine("Done.");
        }

        public static void StartUVLoaderOld(Vita v)
        {
            Console.WriteLine("Loading UVL to memory.");
            long loaduvl = v.GetMethod(false, "LoaderClient.AppMain", "LoadUVL", 0, null);
            if (loaduvl < 0)
            {
                Console.WriteLine("Error getting method.");
                return;
            }
            ValueImpl buffer = v.RunMethod(loaduvl, null, new ValueImpl[] { });

            Console.WriteLine("Allocating space for length of UVL.");
            long alloc = v.GetMethod(true, "System.Runtime.InteropServices.Marshal", "AllocHGlobal", 1, new string[] { "Int32" });
            if (alloc < 0)
            {
                Console.WriteLine("Error getting method.");
                return;
            }
            ValueImpl lenlen = new ValueImpl();
            lenlen.Type = ElementType.I4;
            lenlen.Value = 4; // 4 bytes int
            ValueImpl lenptr = v.RunMethod(alloc, null, new ValueImpl[] { lenlen });

            Console.WriteLine("Getting length of UVL");
            int length = v.GetArrayLength(buffer.Objid);
            if (length < 0)
            {
                Console.WriteLine("Invalid length.");
                return;
            }
            Console.WriteLine("Length is {0} bytes.", length);

            Console.WriteLine("Writing length to heap.");
            long writeint32 = v.GetMethod(true, "System.Runtime.InteropServices.Marshal", "WriteInt32", 2, null);
            if (writeint32 < 0)
            {
                Console.WriteLine("Error getting method.");
                return;
            }
            ValueImpl lenval = new ValueImpl();
            lenval.Type = ElementType.I4;
            lenval.Value = length;
            v.RunMethod(writeint32, null, new ValueImpl[] { lenptr, lenval });

            Console.WriteLine("Getting delegate to pss_code_mem_alloc().");
            /*
            ValueImpl fptr = new ValueImpl();
            fptr.Type = ElementType.ValueType;
            fptr.Klass = lenptr.Klass; // both are IntPtrs
            fptr.Fields = new ValueImpl[] { new ValueImpl() };
            fptr.Fields[0].Type = ElementType.I8;
            fptr.Fields[0].Value = UIntToVitaInt(PSS_CODE_ALLOC_FUNC);
            */
            ValueImpl ftype = new ValueImpl();
            ftype.Type = ElementType.Object;
            ftype.Objid = v.GetTypeObjID(false, "LoaderClient.CodeMemAlloc");
            ValueImpl del_code_alloc = CreateDelegateFromFptr(v, PSS_CODE_ALLOC_FUNC, "System.UInt32", new string[] { "System.UInt32" }, ftype);
            Console.WriteLine("Getting delegate to pss_code_mem_unlock().");
            //fptr.Fields[0].Value = UIntToVitaInt(PSS_CODE_UNLOCK);
            ftype.Objid = v.GetTypeObjID(false, "LoaderClient.CodeMemUnlock");
            ValueImpl del_code_unlock = CreateDelegateFromFptr(v, PSS_CODE_UNLOCK, "System.Void", new string[] {}, ftype);
            //ValueImpl del_code_unlock = v.RunMethod(delforfptr, null, new ValueImpl[] { fptr, ftype });
            Console.WriteLine("Getting delegate to pss_code_mem_lock().");
            //fptr.Fields[0].Value = UIntToVitaInt(PSS_CODE_LOCK);
            ftype.Objid = v.GetTypeObjID(false, "LoaderClient.CodeMemLock");
            ValueImpl del_code_lock = CreateDelegateFromFptr(v, PSS_CODE_LOCK, "System.Void", new string[] { }, ftype);
            //ValueImpl del_code_lock = v.RunMethod(delforfptr, null, new ValueImpl[] { fptr, ftype });

            Console.WriteLine("Getting helper function to create code block.");
            long alloccode = v.GetMethod(false, "LoaderClient.AppMain", "AllocCode", 3, null);
            if (alloccode < 0)
            {
                Console.WriteLine("Error getting method.");
                return;
            }
            // must be objects
            del_code_alloc.Type = ElementType.Object;
            del_code_unlock.Type = ElementType.Object;

            Console.WriteLine("Getting method to copy payload to executable memory.");
            long copy = v.GetMethod(true, "System.Runtime.InteropServices.Marshal", "Copy", 4, new string[] { "Byte[]", "Int32", "IntPtr", "Int32" });
            if (copy < 0)
            {
                Console.WriteLine("Error getting method.");
                return;
            }
            ValueImpl sti = new ValueImpl();
            sti.Type = ElementType.I4;
            sti.Value = 0;
            buffer.Type = ElementType.Object;

            Console.WriteLine("Getting method to relock code memory.");
            long relock = v.GetMethod(false, "LoaderClient.AppMain", "RelockCode", 1, null);
            if (relock < 0)
            {
                Console.WriteLine("Error getting method.");
                return;
            }
            del_code_lock.Type = ElementType.Object;

            Console.WriteLine("Allocating code.");
            ValueImpl codeheap_ptr = v.RunMethod(alloccode, null, new ValueImpl[] { del_code_alloc, del_code_unlock, lenptr });
            ValueImpl codeheap = new ValueImpl();
            codeheap.Type = ElementType.ValueType;
            codeheap.Klass = lenptr.Klass; // both are IntPtrs
            codeheap.Fields = new ValueImpl[] { new ValueImpl() };
            codeheap.Fields[0].Type = ElementType.I8;
            codeheap.Fields[0].Value = UIntToVitaInt((uint)codeheap_ptr.Value);
            Console.WriteLine("Copying code.");
            v.RunMethod(copy, null, new ValueImpl[] { buffer, sti, codeheap, lenval });
            Console.WriteLine("Locking code.");
            v.RunMethod(relock, null, new ValueImpl[] { del_code_lock });

            //Console.WriteLine ("Waiting for changes to take place...");
            //Thread.Sleep (1000); // must do this since 1.80

            Console.WriteLine("Creating a function delegate on buffer.");
            codeheap.Fields[0].Value = (Int64)codeheap.Fields[0].Value + 1; // thumb2 code
            ftype.Objid = v.GetTypeObjID(false, "LoaderClient.RunCode");
            //ValueImpl del_injected = v.RunMethod(delforfptr, null, new ValueImpl[] { codeheap, ftype });
            ValueImpl del_injected = CreateDelegateFromFptr(v, (uint)codeheap.Fields[0].Value, "System.UInt32", new string[] { }, ftype);

            Console.WriteLine("Getting helper function to execute payload.");
            long executepayload = v.GetMethod(false, "LoaderClient.AppMain", "ExecutePayload", 1, null);
            if (executepayload < 0)
            {
                Console.WriteLine("Error getting method.");
                return;
            }
            del_injected.Type = ElementType.Object; // must be object
            Console.WriteLine("Running payload.");
            v.RunMethod(executepayload, null, new ValueImpl[] { del_injected });
        }

        private static void PrintHexDump(byte[] data, uint size, uint num)
        {
            uint i = 0, j = 0, k = 0, l = 0;
            for (l = size / num, k = 1; l > 0; l /= num, k++)
                ; // find number of zeros to prepend line number
            while (j < size)
            {
                // line number
                Console.Write("{0:X" + k + "}: ", j);
                // hex value
                for (i = 0; i < num; i++, j++)
                {
                    if (j < size)
                    {
                        Console.Write("{0:X2} ", data[j]);
                    }
                    else
                    { // print blank spaces
                        Console.Write("   ");
                    }
                }
                // seperator
                Console.Write("| ");
                // ascii value
                for (i = num; i > 0; i--)
                {
                    if (j - i < size)
                    {
                        Console.Write("{0}", data[j - i] < 32 || data[j - i] > 126 ? "." : Char.ToString((char)data[j - i])); // print only visible characters
                    }
                    else
                    {
                        Console.Write(" ");
                    }
                }
                // new line
                Console.WriteLine();
            }
        }

        private static string GetDumpMemoryPackage()
        {
            Console.WriteLine("Extracting client package.");
            string package = Path.GetTempFileName();
            Stream resPkg;
#if PSM_99
            resPkg = Assembly.GetExecutingAssembly().GetManifestResourceStream("VitaInjector.DumpMemory.psdp");
#else
			resPkg = Assembly.GetExecutingAssembly ().GetManifestResourceStream ("VitaInjector.DumpMemory0.98.psspac");
#endif
            FileStream outPkg = File.OpenWrite(package);
            byte[] buff = new byte[MainClass.BLOCK_SIZE];
            int len;
            while ((len = resPkg.Read(buff, 0, MainClass.BLOCK_SIZE)) > 0)
            {
                outPkg.Write(buff, 0, len);
            }
            resPkg.Close();
            outPkg.Close();

            return package;
        }

        private static string GetLoaderPackage(string toload)
        {
            Console.WriteLine("Copying files.");
            File.Copy("uvloader.bin", "LoaderClient/Application/uvloader.bin", true);
            File.Copy(toload, "LoaderClient/Application/homebrew.self", true);
            Console.WriteLine("Generating package.");
            string package = Path.GetTempFileName();
            NativeFunctions.CreatePackage(package, "LoaderClient");
            File.Delete("LoaderClient/Application/uvloader.bin");
            File.Delete("LoaderClient/Application/homebrew.self");
            return package;
        }
    }

    class VitaConnection : Connection
    {
        private int handle99;
        private VitaSerialPort98 handle98;

        public VitaConnection(string port)
        {
#if PSM_99
            this.handle99 = NativeFunctionsTransport.CreateFile(1, @"\\.\" + port);
            if (this.handle99 < 0)
            {
                throw new IOException("Error opening port for connection.");
            }
#else
			this.handle98 = new VitaSerialPort98 (port);
			ConnectPort ();
#endif
        }

        private void ConnectPort()
        {
            int num = 0;
            while (true)
            {
                try
                {
                    this.handle98.Open();
                    this.handle98.DtrEnable = true;
                    this.handle98.RtsEnable = true;
                    return;
                }
                catch (IOException)
                {
                    this.handle98.Dispose();
                    if ((++num * 50) > 0x2710)
                    {
                        throw;
                    }
                }
                Thread.Sleep(50);
            }
        }

        protected override void TransportClose()
        {
#if PSM_99
            NativeFunctionsTransport.CloseHandle(1, handle99);
            this.handle99 = -1;
#else
			this.handle98.Close();
#endif
        }

        protected override unsafe int TransportReceive(byte[] buf, int buf_offset, int len)
        {
#if PSM_99
            while (this.handle99 != -1)
            {
                int recieve = NativeFunctionsTransport.GetReceiveSize(1, this.handle99);
                uint read = 0;
                if (recieve >= len)
                {
                    fixed (byte* p_buf = buf)
                    {
                        if (NativeFunctionsTransport.ReadFile(1, this.handle99, (IntPtr)(p_buf + buf_offset), (uint)len, out read) == 0)
                        {
                            throw new IOException("Cannot read from Vita.");
                        }
                        else
                        {
                            return (int)read;
                        }
                    }
                }
                //Thread.Sleep(30);
            }
#else
			while (this.handle98.IsOpen)
		    {
		        if (this.handle98.BytesToRead >= len)
		        {
		            return this.handle98.Read(buf, buf_offset, len);
		        }
		        //Thread.Sleep(30);
		    }
#endif
            return 0;
        }

        protected override unsafe int TransportSend(byte[] buf, int buf_offset, int len)
        {
#if PSM_99
            int towrite = len;
            uint written = 0;
            fixed (byte* p_buf = buf)
            {
                while (towrite > 0)
                {
                    if (NativeFunctionsTransport.WriteFile(1, this.handle99, (IntPtr)(p_buf + buf_offset), (uint)towrite, out written) == 0)
                    {
                        throw new IOException("Cannot write to Vita.");
                    }
                    towrite -= (int)written;
                }
            }
#else
			this.handle98.Write(buf, buf_offset, len);
#endif
            return len;
        }

        protected override void TransportSetTimeouts(int send_timeout, int receive_timeout)
        {
            return;
        }
    }

    class ConnEventHandler : IEventHandler
    {
        public void Events(SuspendPolicy suspend_policy, EventInfo[] events)
        {
            foreach (EventInfo e in events)
            {
                Console.WriteLine("Event Recieved: {0}", e.EventType);
            }
        }

        public void VMDisconnect(int req_id, long thread_id, string vm_uri)
        {
            return;
        }

        public void ErrorEvent(object sender, EventArgs e)
        {
            return;
        }
    }

    class Vita
    {
#if PSM_99
        public static string PKG_NAME = "VitaInjectorClient";
#else
		public static string PKG_NAME = "VitaInjectorClient0.98";
#endif
        private string port;
        private long rootdomain = -1, threadid = -1, corlibid = -1, assid = -1;
        private volatile int handle;
        private VitaConnection conn;
        private string package;

        public Vita(string portstr, string packagename)
        {
            this.port = portstr;
            this.package = packagename;
        }

        private static void ConsoleOutput(string message)
        {
            Console.WriteLine("[Vita Output] {0}", message);
        }

        private void HandleConnErrorHandler(object sender, ErrorHandlerEventArgs args)
        {
            Console.WriteLine("Error: {0}", args.ErrorCode);
            switch (args.ErrorCode)
            {
                case ErrorCode.NOT_IMPLEMENTED:
                    throw new NotSupportedException("This request is not supported by the protocol version implemented by the debuggee.");

                case ErrorCode.NOT_SUSPENDED:
                    throw new InvalidOperationException("The vm is not suspended.");

                case ErrorCode.ABSENT_INFORMATION:
                    throw new AbsentInformationException();

                case ErrorCode.NO_SEQ_POINT_AT_IL_OFFSET:
                    throw new ArgumentException("Cannot set breakpoint on the specified IL offset.");

                case ErrorCode.INVALID_FRAMEID:
                    throw new InvalidStackFrameException();

                case ErrorCode.INVALID_OBJECT:
                    throw new ObjectCollectedException();
            }
            throw new NotImplementedException(String.Format("{0}", args.ErrorCode));
        }

        public void Start()
        {
            Console.WriteLine("Waiting for Vita to connect...");
            ScePsmDevDevice? vita = null;
            for (; ; )
            {
                ScePsmDevDevice[] deviceArray = new ScePsmDevDevice[8];
                NativeFunctions.ListDevices(deviceArray);
                foreach (ScePsmDevDevice dev in deviceArray)
                {
                    if (dev.online > 0)
                    {
                        vita = dev;
                        break;
                    }
                }
                if (vita != null)
                {
                    break;
                }
            }
            Guid devId = vita.Value.guid;
            Console.WriteLine("Found Vita {0}, serial: {1}", devId, vita.Value.Name);
            this.handle = NativeFunctions.Connect(ref devId);
            if (this.handle < 0)
            {
                StringBuilder strb = new StringBuilder();
                //NativeFunctions99.GetErrStr (strb);
                Console.WriteLine("Error: {0}", strb.ToString());
                return;
            }
            PsmDeviceConsoleCallback callback = new PsmDeviceConsoleCallback(ConsoleOutput);
            Console.WriteLine("Setting console callback.");
            NativeFunctions.SetConsoleCallback(callback);

            Console.WriteLine("Installing package {0} as {1}.", package, PKG_NAME);
            if (NativeFunctions.Install(this.handle, package, PKG_NAME) != 0)
            {
                Console.WriteLine("Error installing package.");
                return;
            }

            Console.WriteLine("Launching {0}.", PKG_NAME);
            if (NativeFunctions.Launch(this.handle, PKG_NAME, true, false, false, "") != 0)
            {
                Console.WriteLine("Error running application.");
                return;
            }

            Console.WriteLine("Connecting debugger.");
            conn = new VitaConnection(port);
            conn.EventHandler = new ConnEventHandler();
            conn.ErrorHandler += HandleConnErrorHandler;
            conn.Connect();

            Console.WriteLine("Waiting for app to start up...");
            conn.VM_Resume();
            Thread.Sleep(1000);
            Console.WriteLine("Getting variables.");
            rootdomain = conn.RootDomain;
            corlibid = conn.Domain_GetCorlib(rootdomain);
            assid = conn.Domain_GetEntryAssembly(rootdomain);
            foreach (long thread in conn.VM_GetThreads())
            {
                if (conn.Thread_GetName(thread) == "")
                {
                    threadid = thread;
                }
            }
            //Console.WriteLine ("Root Domain: {0}\nCorlib: {1}\nExeAssembly: {2}\nThread: {3}", rootdomain, corlibid, assid, threadid);
            Console.WriteLine("Ready for hacking.");
        }

        public void Stop()
        {
            Console.WriteLine("Stopping debugger.");
            conn.Close();
            conn = null;
            Console.WriteLine("Killing running app.");
            NativeFunctions.Kill(this.handle);
            Console.WriteLine("Uninstalling app.");
            NativeFunctions.Uninstall(this.handle, PKG_NAME);
            Console.WriteLine("Disconnecting Vita.");
            NativeFunctions.Disconnect(this.handle);
        }

        public void Suspend()
        {
            conn.VM_Suspend();
        }

        public void Resume()
        {
            conn.VM_Resume();
        }

        public long GetMethod(bool incorlib, string typename, string methodname, int numparams, string[] paramtypenames)
        {
            long assembly = incorlib ? corlibid : assid;
            long type = conn.Assembly_GetType(assembly, typename, false);
            long[] methods = conn.Type_GetMethods(type);
            foreach (long method in methods)
            {
                string name = conn.Method_GetName(method);
                if (name != methodname)
                    continue;
                ParamInfo info = conn.Method_GetParamInfo(method);
                if (info.param_count != numparams)
                    continue;
                if (paramtypenames != null)
                {
                    bool bad = false;
                    for (int i = 0; i < paramtypenames.Length; i++)
                    {
                        if (conn.Type_GetInfo(info.param_types[i]).name != paramtypenames[i])
                        {
                            bad = true;
                            break;
                        }
                    }
                    if (bad)
                    {
                        continue;
                    }
                }
                return method;
            }
            return -1;
        }

        public ValueImpl RunMethod(long methodid, ValueImpl thisval, ValueImpl[] param)
        {
            return RunMethod(methodid, thisval, param, false);
        }

        // pausing the VM is slow, if we're calling this a million times, only need to pause once
        public ValueImpl RunMethod(long methodid, ValueImpl thisval, ValueImpl[] param, bool paused)
        {
            if (thisval == null)
            {
                thisval = new ValueImpl();
                thisval.Type = (ElementType)0xf0;
            }
            ValueImpl ret, exc;
            if (!paused)
            {
                conn.VM_Suspend(); // must be suspended
            }
            ret = conn.VM_InvokeMethod(threadid, methodid, thisval, param == null ? new ValueImpl[] { } : param, InvokeFlags.NONE, out exc);
            if (!paused)
            {
                conn.VM_Resume();
            }
            if (ret != null)
            {
                return ret;
            }
            if (exc != null)
            {
                long excmeth = GetMethod(true, "System.Exception", "ToString", 0, null);
                exc.Type = ElementType.Object; // must do this stupid mono
                ValueImpl excmsg = RunMethod(excmeth, exc, null, paused);
                Console.WriteLine(conn.String_GetValue(excmsg.Objid));
                throw new TargetException("Error running method.");
            }
            return null;
        }

        public ValueImpl GetField(bool incorlib, string typename, string fieldname)
        {
            long assembly = incorlib ? corlibid : assid;
            long typeid = conn.Assembly_GetType(assembly, typename, false);
            string[] f_names;
            long[] f_types;
            int[] f_attrs;
            long[] fields = conn.Type_GetFields(typeid, out f_names, out f_types, out f_attrs);
            long targetfield = -1;

            int i;
            for (i = 0; i < f_names.Length; i++)
            {
                if (f_names[i] == fieldname)
                {
                    targetfield = fields[i];
                    break;
                }
            }
            if (targetfield < 0)
            {
                return null;
            }
            ValueImpl[] values = conn.Type_GetValues(typeid, new long[] { targetfield }, threadid);
            if (values == null || values.Length == 0)
            {
                return null;
            }
            return values[0];
        }

        public void GetBuffer(long objid, int len, ref byte[] buf)
        {
            if (buf == null)
            {
                buf = new byte[len];
            }
            ValueImpl[] vals = conn.Array_GetValues(objid, 0, MainClass.BLOCK_SIZE);
            for (int i = 0; i < vals.Length; i++)
            {
                buf[i] = (byte)vals[i].Value;
            }
        }

        public void SetBuffer(long objid, byte[] buf, int offset, int len)
        {
            if (buf == null || buf.Length == 0)
                return;
            if (len > buf.Length)
                throw new ArgumentException("len > buf.Length");

            ValueImpl[] vals = new ValueImpl[len];
            for (int i = 0; i < len; i++)
            {
                vals[i] = new ValueImpl();
                vals[i].Type = ElementType.U1;
                vals[i].Value = buf[offset + i];
            }
            conn.Array_SetValues(objid, offset, vals);
        }

        public void SetArray(long objid, ValueImpl[] values)
        {
            conn.Array_SetValues(objid, 0, values);
        }

        public long GetTypeObjID(bool incorlib, string name)
        {
            long assembly = incorlib ? corlibid : assid;
            long tid = conn.Assembly_GetType(assembly, name, true);
            return conn.Type_GetObject(tid);
        }

        public int GetArrayLength(long objid)
        {
            int rank;
            int[] lower_bounds;
            int[] len = conn.Array_GetLength(objid, out rank, out lower_bounds);
            if (rank != 1)
            {
                return -1;
            }
            return len[0];
        }

        public long CreateString(string str)
        {
            return conn.Domain_CreateString(conn.RootDomain, str);
        }

        public long GetCorlibModule()
        {
            return conn.Assembly_GetManifestModule(corlibid);
        }
    }
}
