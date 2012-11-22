using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;

namespace LoaderClient
{
	public class AppMain
	{
		public static readonly string LOG_PATH = "/Temp/uvloader.log";
		
		public static void Connect ()
		{
			Console.WriteLine ("Connected to PC.");
			typeof(NativeFunctions).GetMethods(); // take care of lazy init
			Console.WriteLine ("Loaded assembly for native functions.");
			Thread logWat = new Thread (new ThreadStart (LogWatcher));
			logWat.Start ();
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
			if (!File.Exists (ExploitMain.PAYLOAD_PATH)) {
				Console.WriteLine ("Cannot find UVLoader payload.");
			}
			if (!File.Exists (ExploitMain.HOMEBREW_PATH)) {
				Console.WriteLine ("Cannot find homebrew to load.");
			}
			
			// this thread idle
			for (;;);
		}
	}
	
	// to run with elevated privileges
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
		public static readonly string HOMEBREW_PATH = "/Application/homebrew.self";
		public static readonly string DECRYPTED_PATH = "/Temp/homebrew.self";
		
		public static byte[] LoadPayload ()
		{
			Console.WriteLine ("Decrypting homebrew.");
			File.WriteAllBytes (DECRYPTED_PATH, File.ReadAllBytes (HOMEBREW_PATH));
			byte[] buffer = File.ReadAllBytes (PAYLOAD_PATH);
			Console.WriteLine ("Read {0} bytes", buffer.Length);
			return buffer;
		}
		
		[SecuritySafeCritical]
		public static unsafe void RunExploit ()
		{
			Console.WriteLine ("Loading payload into memory.");
			byte[] buffer = LoadPayload();
			Console.WriteLine ("Allocating space for exploit size.");
			IntPtr buffer_size_ptr = Marshal.AllocHGlobal(4);
			Console.WriteLine ("Writing length.");
			Marshal.WriteInt32 (buffer_size_ptr, buffer.Length);
			Console.WriteLine ("Calling pss_code_mem_alloc.");
			IntPtr code_block = NativeFunctions.CodeMemAlloc(new IntPtr(PSS_CODE_ALLOC_FUNC), buffer_size_ptr);
			Console.WriteLine ("Code block allocated at: 0x{0:X}", code_block.ToInt32());
			Marshal.FreeHGlobal(buffer_size_ptr); // memory management
			Console.WriteLine ("Unlocking code memory.");
			NativeFunctions.CodeMemUnlock(new IntPtr(PSS_CODE_UNLOCK));
			Console.WriteLine ("Copying payload to code memory.");
			Marshal.Copy (buffer, 0, code_block, buffer.Length);
			Console.WriteLine ("Relocking code memory.");
			NativeFunctions.CodeMemLock(new IntPtr(PSS_CODE_LOCK));
			Console.WriteLine ("Running payload.");
			IntPtr exec_code = new IntPtr(code_block.ToInt64() | 1); // thumb code
			int ret = NativeFunctions.RunExploit(exec_code);
			Console.WriteLine ("Payload exited with return value: {0}", ret);
		}
	}
}
