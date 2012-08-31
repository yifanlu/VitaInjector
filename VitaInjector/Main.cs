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
		public static readonly Int64 PSS_CODE_ALLOC_FUNC = -2083199240;
		public static readonly Int64 PSS_CODE_UNLOCK = -2083202120;
		public static readonly Int64 PSS_CODE_LOCK = -2083202040;
		
		public static void PrintHelp ()
		{
			Console.WriteLine (
				"usage: VitaInjector.exe mode (file|address len out) port\n" +
				"    mode:\n" +
				"        i[nject]    inject code and execute it\n" +
				"        d[ump]      dump portion of the memory\n" +
				"    options (inject):\n" +
				"        file        binary file of ARM code\n" +
				"    options (dump):\n" +
				"        address     address to start dumping\n" +
				"        len         length of dump\n" +
				"        out         file to dump to\n" +
				"    options:\n" +
				"        port        Vita's COM port\n" +
				"ex:\n" +
				"    VitaInjector.exe i code.bin COM5\n" +
				"    VitaInjector.exe d 0x81000000 0x100 COM5\n"
			);
		}
		
		public static void Main (string[] args)
		{
			if (args.Length < 1) {
				Console.WriteLine ("error: arguments required.");
				PrintHelp ();
				return;
			}
			switch (args [0].ToCharArray () [0]) {
			case 'i':
				InjectMain (args);
				break;
			case 'd':
				DumpMain (args);
				break;
			case '?':
			case 'h':
			default:
				PrintHelp ();
				break;
			}
		}
		
		public static void InjectMain (string[] args)
		{
			if (args.Length < 3) {
				Console.WriteLine ("error: not enough arguments.");
				PrintHelp ();
				return;
			}
			string port = args [2];
			byte[] payload = File.ReadAllBytes (args [1]);
			Vita v = new Vita (port);
			v.Start ();
			AlertClient (v);
			StartInjection (v, payload);
			Thread.Sleep (50000); // give it a few seconds
			v.Stop ();
		}
		
		public static void DumpMain (string[] args)
		{
			if (args.Length < 5) {
				Console.WriteLine ("error: not enough arguments.");
				PrintHelp ();
				return;
			}
			string port = args [4];
			uint addr, len;
			FileStream dump;
			addr = Convert.ToUInt32 (args [1], args [1].StartsWith ("0x") ? 16 : 10);
			len = Convert.ToUInt32 (args [2], args [2].StartsWith ("0x") ? 16 : 10);
			dump = File.OpenWrite (args [3]);
			Vita v = new Vita (port);
			v.Start ();
			AlertClient (v);
			StartDump (v, addr, len, dump);
			Thread.Sleep (5000); // give it a few seconds
			v.Stop ();
		}
		
		public static void AlertClient (Vita v)
		{
			Console.WriteLine ("Alerting Vita of connection.");
			long connectmethod = v.GetMethod (false, "VitaInjectorClient.AppMain", "Connect", 0, null);
			if (connectmethod < 0) {
				Console.WriteLine ("Cannot find Connect() methond on device.");
			}
			v.RunMethod (connectmethod, null, null);
		}
		
		public static void StartDump (Vita v, uint addr, uint len, FileStream dump)
		{
			if (len == 0) {
				// dump all of ram
				len = 0xFFFFFFFF - addr;
			}
			// weird address format for IntPtr on vita
			Int64 src_addr = BitConverter.ToInt64 (new byte[]{0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF}, 0);
			src_addr += addr;
			ValueImpl dest = v.GetField (false, "VitaInjectorClient.AppMain", "dest");
			dest.Type = ElementType.Object; // must be done
			ValueImpl src = v.GetField (false, "VitaInjectorClient.AppMain", "src");
			if (dest == null) {
				Console.WriteLine ("Cannot find buffer to write to.");
				return;
			}
			if (src == null) {
				Console.WriteLine ("Cannot find pointer to read from.");
				return;
			}
			long copymethod = v.GetMethod (true, "System.Runtime.InteropServices.Marshal", "Copy", 4, new string[]{"IntPtr", "Byte[]", "Int32", "Int32"});
			if (copymethod < 0) {
				Console.WriteLine ("Cannot find Copy method.");
				return;
			}
			byte[] block = new byte[BLOCK_SIZE];
			ValueImpl sti = new ValueImpl ();
			ValueImpl dlen = new ValueImpl ();
			sti.Type = ElementType.I4;
			dlen.Type = ElementType.I4;
			sti.Value = 0;
			dlen.Value = BLOCK_SIZE;
			src.Fields [0].Value = src_addr;
			v.Suspend ();
			for (int d = 0; d * BLOCK_SIZE < len; d++) {
				try {
					Console.WriteLine ("Dumping 0x{0:X}", src.Fields [0].Value);
					ValueImpl ret = v.RunMethod (copymethod, null, new ValueImpl[]{src, dest, sti, dlen}, true);
					if (ret == null) {
						throw new TargetException ("Method never returned.");
					}
					v.GetArray (dest.Objid, BLOCK_SIZE, ref block);
					PrintHexDump (block, (uint)BLOCK_SIZE, 16);
					int num = BLOCK_SIZE;
					if (d * BLOCK_SIZE + num > len)
						num = (int)(len - d * BLOCK_SIZE);
					dump.Write (block, 0, num);
				} catch (Exception ex) {
					if (len > 0) {
						Console.WriteLine ("Error dumping:\n{0}", ex.ToString ());
						return;
					} else {
						Console.WriteLine ("Continuing on error.");
					}
				}
				// next block to dump
				src.Fields [0].Value = (Int64)src.Fields [0].Value + BLOCK_SIZE;
				if (d % 1000 == 0) {
					// must be done or app will freeze
					v.Resume ();
					v.Suspend ();
				}
			}
			v.Resume ();
		}
		
		public static void StartInjection (Vita v, byte[] payload)
		{
			Console.WriteLine ("Allocating space for length int.");
			long alloc = v.GetMethod (true, "System.Runtime.InteropServices.Marshal", "AllocHGlobal", 1, new string[]{"Int32"});
			if (alloc < 0) {
				Console.WriteLine ("Error getting method.");
				return;
			}
			ValueImpl lenlen = new ValueImpl ();
			lenlen.Type = ElementType.I4;
			lenlen.Value = 4; // 4 bytes int
			ValueImpl lenptr = v.RunMethod (alloc, null, new ValueImpl[]{lenlen});
			
			Console.WriteLine ("Writing length to heap.");
			long writeint32 = v.GetMethod (true, "System.Runtime.InteropServices.Marshal", "WriteInt32", 2, null);
			if (writeint32 < 0) {
				Console.WriteLine ("Error getting method.");
				return;
			}
			ValueImpl lenval = new ValueImpl ();
			lenval.Type = ElementType.I4;
			lenval.Value = payload.Length;
			v.RunMethod (writeint32, null, new ValueImpl[]{lenptr, lenval});
			
			Console.WriteLine ("Getting helper function to create buffer on Vita.");
			long createbuffer = v.GetMethod (false, "VitaInjectorClient.AppMain", "CreateBuffer", 1, null);
			if (createbuffer < 0) {
				Console.WriteLine ("Error getting method.");
				return;
			}
			ValueImpl buffer = v.RunMethod (createbuffer, null, new ValueImpl[]{lenval});
			
			Console.WriteLine ("Writing payload to Vita's buffer.");
			for (int k = 0; k < payload.Length; k += BLOCK_SIZE) {
				Console.WriteLine ("Writing 0x{0:X}...", k);
				v.SetArray (buffer.Objid, payload, k, (k + BLOCK_SIZE > payload.Length) ? payload.Length - k : BLOCK_SIZE);
			}
			
			Console.WriteLine ("Getting delegate to pss_code_mem_alloc().");
			long delforfptr = v.GetMethod (true, "System.Runtime.InteropServices.Marshal", "GetDelegateForFunctionPointer", 2, null);
			if (delforfptr < 0) {
				Console.WriteLine ("Error getting method.");
				return;
			}
			ValueImpl fptr = new ValueImpl ();
			fptr.Type = ElementType.ValueType;
			fptr.Klass = lenptr.Klass; // both are IntPtrs
			fptr.Fields = new ValueImpl[]{ new ValueImpl () };
			fptr.Fields [0].Type = ElementType.I8;
			fptr.Fields [0].Value = PSS_CODE_ALLOC_FUNC;
			ValueImpl ftype = new ValueImpl ();
			ftype.Type = ElementType.Object;
			ftype.Objid = v.GetTypeObjID (false, "VitaInjectorClient.pss_code_mem_alloc");
			ValueImpl del_code_alloc = v.RunMethod (delforfptr, null, new ValueImpl[]{fptr, ftype});
			Console.WriteLine ("Getting delegate to pss_code_mem_unlock().");
			fptr.Fields [0].Value = PSS_CODE_UNLOCK;
			ftype.Objid = v.GetTypeObjID (false, "VitaInjectorClient.pss_code_mem_unlock");
			ValueImpl del_code_unlock = v.RunMethod (delforfptr, null, new ValueImpl[]{fptr, ftype});
			Console.WriteLine ("Getting delegate to pss_code_mem_lock().");
			fptr.Fields [0].Value = PSS_CODE_LOCK;
			ftype.Objid = v.GetTypeObjID (false, "VitaInjectorClient.pss_code_mem_lock");
			ValueImpl del_code_lock = v.RunMethod (delforfptr, null, new ValueImpl[]{fptr, ftype});
			
			Console.WriteLine ("Getting method to relock code memory.");
			long relock = v.GetMethod (false, "VitaInjectorClient.AppMain", "RelockCode", 1, null);
			if (relock < 0) {
				Console.WriteLine ("Error getting method.");
				return;
			}
			del_code_lock.Type = ElementType.Object;
			
			Console.WriteLine ("Getting helper function to create code block.");
			long alloccode = v.GetMethod (false, "VitaInjectorClient.AppMain", "AllocCode", 3, null);
			if (alloccode < 0) {
				Console.WriteLine ("Error getting method.");
				return;
			}
			// must be objects
			del_code_alloc.Type = ElementType.Object;
			del_code_unlock.Type = ElementType.Object;
			
			Console.WriteLine ("Getting method to copy payload to executable memory.");
			long copy = v.GetMethod (true, "System.Runtime.InteropServices.Marshal", "Copy", 4, new string[]{"Byte[]", "Int32", "IntPtr", "Int32"});
			if (copy < 0) {
				Console.WriteLine ("Error getting method.");
				return;
			}
			ValueImpl sti = new ValueImpl ();
			sti.Type = ElementType.I4;
			sti.Value = 0;
			buffer.Type = ElementType.Object;
			
			Console.WriteLine ("Running methods to inject payload.");
			ValueImpl codeheap = v.RunMethod (alloccode, null, new ValueImpl[]{del_code_alloc, del_code_unlock, lenptr});
			v.RunMethod (copy, null, new ValueImpl[]{buffer, sti, codeheap, lenval});
			v.RunMethod (relock, null, new ValueImpl[]{del_code_lock});
			
			Thread.Sleep (5000); // must do this since 1.80
			
			Console.WriteLine ("Creating a function delegate on buffer.");
			codeheap.Fields [0].Value = (Int64)codeheap.Fields [0].Value + 1; // thumb2 code
			ftype.Objid = v.GetTypeObjID (false, "VitaInjectorClient.RunCode");
			ValueImpl del_injected = v.RunMethod (delforfptr, null, new ValueImpl[]{codeheap, ftype});
			
			Console.WriteLine ("Getting delegate to output text.");
			ValueImpl del_output = v.GetField (false, "VitaInjectorClient.AppMain", "output");
			
			Console.WriteLine ("Getting function to turn delegate to function pointer.");
			long deltofptr = v.GetMethod (true, "System.Runtime.InteropServices.Marshal", "GetFunctionPointerForDelegate", 1, null);
			if (deltofptr < 0) {
				Console.WriteLine ("Error getting method.");
				return;
			}
			del_output.Type = ElementType.Object;
			ValueImpl fptr_output = v.RunMethod (deltofptr, null, new ValueImpl[]{del_output});
			
			Console.WriteLine ("Getting helper function to execute payload.");
			long executepayload = v.GetMethod (false, "VitaInjectorClient.AppMain", "ExecutePayload", 2, null);
			if (executepayload < 0) {
				Console.WriteLine ("Error getting method.");
				return;
			}
			del_injected.Type = ElementType.Object; // must be object
			Console.WriteLine ("Running payload.");
			v.RunMethod (executepayload, null, new ValueImpl[]{del_injected, fptr_output});
		}
		
		private static void PrintHexDump (byte[] data, uint size, uint num)
		{
			uint i = 0, j = 0, k = 0, l = 0;
			for (l = size/num, k = 1; l > 0; l/=num, k++)
				; // find number of zeros to prepend line number
			while (j < size) {
				// line number
				Console.Write ("{0:X" + k + "}: ", j);
				// hex value
				for (i = 0; i < num; i++, j++) {
					if (j < size) {
						Console.Write ("{0:X2} ", data [j]);
					} else { // print blank spaces
						Console.Write ("   ");
					}
				}
				// seperator
				Console.Write ("| ");
				// ascii value
				for (i = num; i > 0; i--) {
					if (j - i < size) {
						Console.Write ("{0}", data [j - i] < 32 || data [j - i] > 126 ? "." : Char.ToString ((char)data [j - i])); // print only visible characters
					} else {
						Console.Write (" ");
					}
				}
				// new line
				Console.WriteLine ();
			}
		}
	}
	
	class VitaConnection : Connection
	{
		private int handle99;
		private VitaSerialPort98 handle98;
		
		public VitaConnection (string port)
		{
#if PSM_99
			this.handle99 = NativeFunctionsTransport.CreateFile (1, @"\\.\" + port);
			if (this.handle99 < 0) {
				throw new IOException ("Error opening port for connection.");
			}
#else
			this.handle98 = new VitaSerialPort98 (port);
			ConnectPort ();
#endif
		}
		
		private void ConnectPort ()
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
		
		protected override void TransportClose ()
		{
#if PSM_99
			NativeFunctionsTransport.CloseHandle (1, handle99);
			this.handle99 = -1;
#else
			this.handle98.Close();
#endif
		}
		
		protected override unsafe int TransportReceive (byte[] buf, int buf_offset, int len)
		{
#if PSM_99
			while (this.handle99 != -1) {
				int recieve = NativeFunctionsTransport.GetReceiveSize (1, this.handle99);
				uint read = 0;
				if (recieve >= len) {
					fixed (byte* p_buf = buf) {
						if (NativeFunctionsTransport.ReadFile (1, this.handle99, (IntPtr)(p_buf + buf_offset), (uint)len, out read) == 0) {
							throw new IOException ("Cannot read from Vita.");
						} else {
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
		
		protected override unsafe int TransportSend (byte[] buf, int buf_offset, int len)
		{
#if PSM_99
			int towrite = len;
			uint written = 0;
			fixed (byte* p_buf = buf) {
				while (towrite > 0) {
					if (NativeFunctionsTransport.WriteFile (1, this.handle99, (IntPtr)(p_buf + buf_offset), (uint)towrite, out written) == 0) {
						throw new IOException ("Cannot write to Vita.");
					}
					towrite -= (int)written;
				}
			}
#else
			this.handle98.Write(buf, buf_offset, len);
#endif
			return len;
		}
		
		protected override void TransportSetTimeouts (int send_timeout, int receive_timeout)
		{
			return;
		}
	}
	
	class ConnEventHandler : IEventHandler
	{
		public void Events (SuspendPolicy suspend_policy, EventInfo[] events)
		{
			foreach (EventInfo e in events) {
				Console.WriteLine ("Event Recieved: {0}", e.EventType);
			}
		}

		public void VMDisconnect (int req_id, long thread_id, string vm_uri)
		{
			return;
		}

		public void ErrorEvent (object sender, EventArgs e)
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
		
		public Vita (string portstr)
		{
			this.port = portstr;
		}
		
		private static void ConsoleOutput (string message)
		{
			Console.WriteLine ("[Vita Output] {0}", message);
		}
		
		private void HandleConnErrorHandler (object sender, ErrorHandlerEventArgs args)
		{
			Console.WriteLine ("Error: {0}", args.ErrorCode);
			switch (args.ErrorCode) {
			case ErrorCode.NOT_IMPLEMENTED:
				throw new NotSupportedException ("This request is not supported by the protocol version implemented by the debuggee.");
		
			case ErrorCode.NOT_SUSPENDED:
				throw new InvalidOperationException ("The vm is not suspended.");
		
			case ErrorCode.ABSENT_INFORMATION:
				throw new AbsentInformationException ();
		
			case ErrorCode.NO_SEQ_POINT_AT_IL_OFFSET:
				throw new ArgumentException ("Cannot set breakpoint on the specified IL offset.");
		
			case ErrorCode.INVALID_FRAMEID:
				throw new InvalidStackFrameException ();
		
			case ErrorCode.INVALID_OBJECT:
				throw new ObjectCollectedException ();
			}
			throw new NotImplementedException (String.Format ("{0}", args.ErrorCode));
		}
		
		public void Start ()
		{
			Console.WriteLine ("Waiting for Vita to connect...");
			ScePsmDevDevice? vita = null;
			for (; ;) {
				ScePsmDevDevice[] deviceArray = new ScePsmDevDevice[8];
				NativeFunctions.ListDevices (deviceArray);
				foreach (ScePsmDevDevice dev in deviceArray) {
					if (dev.online > 0) {
						vita = dev;
						break;
					}
				}
				if (vita != null) {
					break;
				}
			}
			Guid devId = vita.Value.guid;
			Console.WriteLine ("Found Vita {0}, serial: {1}", devId, vita.Value.Name);
			this.handle = NativeFunctions.Connect (ref devId);
			if (this.handle < 0) {
				StringBuilder strb = new StringBuilder ();
				//NativeFunctions99.GetErrStr (strb);
				Console.WriteLine ("Error: {0}", strb.ToString ());
				return;
			}
			PsmDeviceConsoleCallback callback = new PsmDeviceConsoleCallback (ConsoleOutput);
			Console.WriteLine ("Setting console callback.");
			NativeFunctions.SetConsoleCallback (callback);
			
			Console.WriteLine ("Extracting client package.");
			string package = Path.GetTempFileName ();
			Stream resPkg;
#if PSM_99
			resPkg = Assembly.GetExecutingAssembly ().GetManifestResourceStream ("VitaInjector.VitaInjectorClient.psdp");
#else
			resPkg = Assembly.GetExecutingAssembly ().GetManifestResourceStream ("VitaInjector.VitaInjectorClient0.98.psspac");
#endif
			FileStream outPkg = File.OpenWrite (package);
			byte[] buff = new byte[MainClass.BLOCK_SIZE];
			int len;
			while ((len = resPkg.Read(buff, 0, MainClass.BLOCK_SIZE)) > 0) {
				outPkg.Write (buff, 0, len);
			}
			resPkg.Close ();
			outPkg.Close ();
			
			Console.WriteLine ("Installing package {0} as {1}.", package, PKG_NAME);
			if (NativeFunctions.Install (this.handle, package, PKG_NAME) != 0) {
				Console.WriteLine ("Error installing package.");
				return;
			}
			
			Console.WriteLine ("Launching {0}.", PKG_NAME);
			if (NativeFunctions.Launch (this.handle, PKG_NAME, true, false, false, "") != 0) {
				Console.WriteLine ("Error running application.");
				return;
			}
			
			Console.WriteLine ("Connecting debugger.");
			conn = new VitaConnection (port);
			conn.EventHandler = new ConnEventHandler ();
			conn.ErrorHandler += HandleConnErrorHandler;
			conn.Connect ();
			
			Console.WriteLine ("Waiting for app to start up.");
			conn.VM_Resume ();
			Thread.Sleep (15000);
			Console.WriteLine ("Getting variables.");
			rootdomain = conn.RootDomain;
			corlibid = conn.Domain_GetCorlib (rootdomain);
			assid = conn.Domain_GetEntryAssembly (rootdomain);
			foreach (long thread in conn.VM_GetThreads()) {
				if (conn.Thread_GetName (thread) == "") {
					threadid = thread;
				}
			}
			Console.WriteLine ("Root Domain: {0}\nCorlib: {1}\nExeAssembly: {2}\nThread: {3}", rootdomain, corlibid, assid, threadid);
			Console.WriteLine ("Ready for hacking.");
		}
		
		public void Stop ()
		{
			Console.WriteLine ("Stopping debugger.");
			conn.Close ();
			conn = null;
			Console.WriteLine ("Killing running app.");
			NativeFunctions.Kill (this.handle);
			Console.WriteLine ("Uninstalling app.");
			NativeFunctions.Uninstall (this.handle, PKG_NAME);
			Console.WriteLine ("Disconnecting Vita.");
			NativeFunctions.Disconnect (this.handle);
		}
		
		public void Suspend ()
		{
			conn.VM_Suspend ();
		}
		
		public void Resume ()
		{
			conn.VM_Resume ();
		}
		
		public long GetMethod (bool incorlib, string typename, string methodname, int numparams, string[] paramtypenames)
		{
			long assembly = incorlib ? corlibid : assid;
			long type = conn.Assembly_GetType (assembly, typename, false);
			long[] methods = conn.Type_GetMethods (type);
			foreach (long method in methods) {
				string name = conn.Method_GetName (method);
				if (name != methodname)
					continue;
				ParamInfo info = conn.Method_GetParamInfo (method);
				if (info.param_count != numparams)
					continue;
				if (paramtypenames != null) {
					bool bad = false;
					for (int i = 0; i < paramtypenames.Length; i++) {
						if (conn.Type_GetInfo (info.param_types [i]).name != paramtypenames [i]) {
							bad = true;
							break;
						}
					}
					if (bad) {
						continue;
					}
				}
				return method;
			}
			return -1;
		}
		
		public ValueImpl RunMethod (long methodid, ValueImpl thisval, ValueImpl[] param)
		{
			return RunMethod (methodid, thisval, param, false);
		}
		
		// pausing the VM is slow, if we're calling this a million times, only need to pause once
		public ValueImpl RunMethod (long methodid, ValueImpl thisval, ValueImpl[] param, bool paused)
		{
			if (thisval == null) {
				thisval = new ValueImpl ();
				thisval.Type = (ElementType)0xf0;
			}
			ValueImpl ret, exc;
			if (!paused) {
				conn.VM_Suspend (); // must be suspended
			}
			ret = conn.VM_InvokeMethod (threadid, methodid, thisval, param == null ? new ValueImpl[]{} : param, InvokeFlags.NONE, out exc);
			if (!paused) {
				conn.VM_Resume ();
			}
			if (ret != null) {
				return ret;
			}
			if (exc != null) {
				long excmeth = GetMethod (true, "System.Exception", "ToString", 0, null);
				exc.Type = ElementType.Object; // must do this stupid mono
				ValueImpl excmsg = RunMethod (excmeth, exc, null);
				Console.WriteLine (conn.String_GetValue (excmsg.Objid));
				throw new TargetException ("Error running method.");
			}
			return null;
		}
		
		public ValueImpl GetField (bool incorlib, string typename, string fieldname)
		{
			long assembly = incorlib ? corlibid : assid;
			long typeid = conn.Assembly_GetType (assembly, typename, false);
			string[] f_names;
			long[] f_types;
			int[] f_attrs;
			long[] fields = conn.Type_GetFields (typeid, out f_names, out f_types, out f_attrs);
			long targetfield = -1;
			
			int i;
			for (i = 0; i < f_names.Length; i++) {
				if (f_names [i] == fieldname) {
					targetfield = fields [i];
					break;
				}
			}
			if (targetfield < 0) {
				return null;
			}
			ValueImpl[] values = conn.Type_GetValues (typeid, new long[]{targetfield}, threadid);
			if (values == null || values.Length == 0) {
				return null;
			}
			return values [0];
		}
		
		public void GetArray (long objid, int len, ref byte[] buf)
		{
			if (buf == null) {
				buf = new byte[len];
			}
			ValueImpl[] vals = conn.Array_GetValues (objid, 0, MainClass.BLOCK_SIZE);
			for (int i = 0; i < vals.Length; i++) {
				buf [i] = (byte)vals [i].Value;
			}
		}
		
		public void SetArray (long objid, byte[] buf, int offset, int len)
		{
			if (buf == null || buf.Length == 0)
				return;
			if (len > buf.Length)
				throw new ArgumentException ("len > buf.Length");
			
			ValueImpl[] vals = new ValueImpl[len];
			for (int i = 0; i < len; i++) {
				vals [i] = new ValueImpl ();
				vals [i].Type = ElementType.U1;
				vals [i].Value = buf [offset + i];
			}
			conn.Array_SetValues (objid, offset, vals);
		}
		
		public long GetTypeObjID (bool incorlib, string name)
		{
			long assembly = incorlib ? corlibid : assid;
			long tid = conn.Assembly_GetType (assembly, name, true);
			return conn.Type_GetObject (tid);
		}
	}
}
