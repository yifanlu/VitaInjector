using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Security;

namespace LoaderClient
{
	public delegate uint RunCode ();

	public delegate uint CodeMemAlloc (uint length);

	public delegate void CodeMemUnlock ();
	
	public delegate void CodeMemLock ();
	
	public class AppMain
	{
		public static readonly string UVLOADER_PATH = "/Application/uvloader.bin";
		public static readonly string HOMEBREW_PATH = "/Application/homebrew.self";
		public static readonly string DECRYPTED_PATH = "/Temp/homebrew.self";
		public static readonly string LOG_PATH = "/Temp/uvloader.log";
		public volatile static byte[] buffer;
		
		public static void Connect ()
		{
			Console.WriteLine ("Connected to PC.");
			Thread logWat = new Thread (new ThreadStart (LogWatcher));
			logWat.Start ();
		}
		
		public static uint AllocCode (CodeMemAlloc alloc, CodeMemUnlock unlock, IntPtr p_len)
		{
			Console.WriteLine ("Creating code block.");
			uint ret = alloc ((uint)p_len.ToInt32());
			Console.WriteLine ("Allocated at 0x{0:X}. Unlocking.", ret);
			unlock ();
			Console.WriteLine ("Code block unlocked for writing.");
			return ret;
		}
		
		public static void RelockCode (CodeMemLock relock)
		{
			relock ();
			Console.WriteLine ("Relocked code block.");
		}
		
		public static byte[] LoadUVL ()
		{
			buffer = File.ReadAllBytes (UVLOADER_PATH);
			Console.WriteLine ("Read {0} bytes", buffer.Length);
			return buffer;
		}
		
		public static void ExecutePayload (RunCode run)
		{
			Console.WriteLine ("Decrypting homebrew.");
			File.WriteAllBytes (DECRYPTED_PATH, File.ReadAllBytes (HOMEBREW_PATH));
			Console.WriteLine ("Executing payload.");
			uint ret = run ();
			Console.WriteLine ("Payload returned value: 0x{0:X}", ret);
		}
		
		public static void LogWatcher ()
		{
			FileStream log = new FileStream (LOG_PATH, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);
#if NETWORK_LOGGING
			TcpListener listener = new TcpListener (IPAddress.Parse("0.0.0.0"), 5555);
			listener.Start ();
			Console.WriteLine ("Waiting for connection.");
			Socket sock = listener.AcceptSocket ();
			Console.WriteLine ("Found client.");
			NetworkStream netstream = new NetworkStream (sock);
			StreamWriter sw = new StreamWriter(netstream);
#endif
			StreamReader sr = new StreamReader (log);
			Console.WriteLine ("Log watcher started.");
			string line;
			for (;;) {
				line = sr.ReadLine ();
				if (line != null) {
#if NETWORK_LOGGING
					sw.WriteLine(line);
#else
					Console.Write (line);
#endif
					continue;
				}
				//Thread.Sleep (1000);
			}
		}
		
		public static void Main (string[] args)
		{
			if (!File.Exists (UVLOADER_PATH)) {
				Console.WriteLine ("Cannot find UVLoader payload.");
			}
			if (!File.Exists (HOMEBREW_PATH)) {
				Console.WriteLine ("Cannot find homebrew to load.");
			}
			
			// this thread idle
			for (;;);
		}
	}
	
	// to be run with elevated privileges
	public static class ExploitMain
	{
		public delegate uint CodeAlloc (uint length);
	
		public delegate void CodeUnlock ();
		
		public delegate void CodeLock ();
		
		public delegate void Payload ();
		
        public static readonly uint PSS_CODE_ALLOC_FUNC = 0x82B27695;
        public static readonly uint PSS_CODE_UNLOCK = 0x82B27669;
        public static readonly uint PSS_CODE_LOCK = 0x82B27641;
		public static readonly string PAYLOAD_PATH = "/Application/uvloader.bin";
		public volatile static byte[] buffer;
		
		[SecuritySafeCritical]
		public static byte[] LoadPayload ()
		{
			buffer = File.ReadAllBytes (PAYLOAD_PATH);
			Console.WriteLine ("Read {0} bytes", buffer.Length);
			return buffer;
		}
		
		[SecuritySafeCritical]
		public static void RunExploit ()
		{
			Console.WriteLine ("Loading payload into memory.");
			Console.WriteLine(typeof(ILGenerator).GetMethods());
			LoadPayload();
			Console.WriteLine ("Allocating space for exploit size.");
			IntPtr buffer_size_ptr = Marshal.AllocHGlobal(4);
			Console.WriteLine ("Writing length.");
			Marshal.WriteInt32 (buffer_size_ptr, buffer.Length);
			Console.WriteLine ("Creating delegate to pss_code_mem_alloc.");
			CodeAlloc code_alloc = (CodeAlloc)CreateDelegateFromFptr(new IntPtr(PSS_CODE_ALLOC_FUNC), typeof(CodeAlloc), typeof(uint), new Type[]{ typeof(uint) });
			Console.WriteLine ("Calling pss_code_mem_alloc.");
			IntPtr code_block = new IntPtr(code_alloc((uint)buffer_size_ptr.ToInt32()));
			Marshal.FreeHGlobal(buffer_size_ptr); // memory management
			Console.WriteLine ("Creating delegate to pss_code_mem_unlock.");
			CodeUnlock code_unlock = (CodeUnlock)CreateDelegateFromFptr(new IntPtr(PSS_CODE_UNLOCK), typeof(CodeUnlock), typeof(void), new Type[]{});
			Console.WriteLine ("Unlocking code memory.");
			code_unlock ();
			Console.WriteLine ("Copying payload to code memory.");
			Marshal.Copy (buffer, 0, code_block, buffer.Length);
			Console.WriteLine ("Creating delegate to pss_code_mem_lock.");
			CodeLock code_lock = (CodeLock)CreateDelegateFromFptr(new IntPtr(PSS_CODE_LOCK), typeof(CodeLock), typeof(void), new Type[]{});
			Console.WriteLine ("Relocking code memory.");
			code_lock ();
			Console.WriteLine ("Getting delegate to start of payload.");
			Payload payload = (Payload)CreateDelegateFromFptr(code_block, typeof(Payload), typeof(void), new Type[]{});
			Console.WriteLine ("Running payload.");
			payload();
		}
		
		[SecuritySafeCritical]
		public static Delegate CreateDelegateFromFptr (IntPtr function, Type del_t, Type ret_t, Type[] params_t)
		{
			DynamicMethod method = new DynamicMethod("method_" + function.ToInt32(), ret_t, params_t, ret_t.Module);
			ILGenerator gen = method.GetILGenerator();
			for(int i = 0; i < params_t.Length; i++)
			{
				gen.Emit(OpCodes.Ldarg, i);
			}
			gen.Emit(OpCodes.Ldc_I4, function.ToInt32());
			gen.Emit(OpCodes.Conv_I);
			gen.EmitCalli(OpCodes.Calli, CallingConvention.StdCall, ret_t, params_t);
			gen.Emit(OpCodes.Ret);
			
			Delegate del = method.CreateDelegate(del_t);
			return del;
		}
	}
}
