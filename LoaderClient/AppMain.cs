using System;
using System.IO;
using System.Threading;
using System.Net;
using System.Net.Sockets;

namespace VitaInjectorClient
{
	public delegate uint RunCode ();

	public delegate IntPtr pss_code_mem_alloc (IntPtr length);

	public delegate void pss_code_mem_unlock ();
	
	public delegate void pss_code_mem_lock ();
	
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
		
		public static IntPtr AllocCode (pss_code_mem_alloc alloc, pss_code_mem_unlock unlock, IntPtr p_len)
		{
			Console.WriteLine ("Creating code block.");
			IntPtr ret = alloc (p_len);
			Console.WriteLine ("Allocated at 0x{0:X}. Unlocking.", ret.ToInt32 ());
			unlock ();
			Console.WriteLine ("Code block unlocked for writing.");
			return ret;
		}
		
		public static void RelockCode (pss_code_mem_lock relock)
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
}
