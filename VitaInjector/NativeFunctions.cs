using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VitaInjector
{
	public enum PsmDeviceType
	{
		Simulator,
		PsVita,
		Android
	}

	[StructLayout(LayoutKind.Sequential)]
    public unsafe struct ScePsmDevDevice
	{
		public Guid guid;
		public PsmDeviceType type;
		public int online;
		public fixed byte name[0x80];

		public string Name {
			get {
				fixed (byte* p_name = name) {
					return Marshal.PtrToStringAnsi ((IntPtr)p_name);
				}
			}
		}
	}

	public enum ScePsmDevErrorCode
	{
		CannotAccessStorage = -2147418107,
		InvalidAppID = -2147418109,
		InvalidFilepath = -2147418108,
		InvalidPackage = -2147418110,
		NoConnection = -2147418111,
		Ok = 0,
		StorageFull = -2147418106,
		VersionHost = -2147418100,
		VersionTarget = -2147418099
	}

	[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false)]
    public delegate void PsmDeviceConsoleCallback (string message);
	
	public class NativeFunctionsTransport
	{
		[DllImport("host_transport64.dll", EntryPoint="scePsmHTCloseHandle")]
		public static extern int CloseHandle (int src, int handle);

		[DllImport("host_transport64.dll", EntryPoint="scePsmHTCreateFile", CharSet=CharSet.Ansi)]
		public static extern int CreateFile (int src, string comname);

		[DllImport("host_transport64.dll", EntryPoint="scePsmHTGetReceiveSize")]
		public static extern int GetReceiveSize (int src, int hFile);

		[DllImport("host_transport64.dll", EntryPoint="scePsmHTReadFile", SetLastError=true)]
		public static extern int ReadFile (int src, int hFile, IntPtr lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead);

		[DllImport("host_transport64.dll", EntryPoint="scePsmHTWriteFile", SetLastError=true)]
		public static extern int WriteFile (int src, int hFile, IntPtr lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten);
	}
	
	public class NativeFunctions98
	{
		private const string NATIVE_DLL = "pss_device64.dll";
		
		[DllImport(NATIVE_DLL, EntryPoint="scePssDevConnect")]
	    public static extern int Connect(ref Guid deviceGuid);
		
	    [DllImport(NATIVE_DLL, EntryPoint="scePssDevCreatePackage", CharSet=CharSet.Ansi)]
	    public static extern ScePsmDevErrorCode CreatePackage(string packageFile, string dirForPack);
		
	    [DllImport(NATIVE_DLL, EntryPoint="scePssDevDisconnect")]
	    public static extern ScePsmDevErrorCode Disconnect(int handle);
		
	    [DllImport(NATIVE_DLL, EntryPoint="scePssDevInstall", CharSet=CharSet.Ansi)]
	    public static extern ScePsmDevErrorCode Install(int handle, string packageFile, string appId);
		
	    [DllImport(NATIVE_DLL, EntryPoint="scePssDevKill")]
	    public static extern ScePsmDevErrorCode Kill(int handle);
		
	    [DllImport(NATIVE_DLL, EntryPoint="scePssDevLaunch", CharSet=CharSet.Ansi)]
	    public static extern ScePsmDevErrorCode Launch(int handle, string appId, bool debug, bool profile, bool keepnet, string arg);
		
	    [DllImport(NATIVE_DLL, EntryPoint="scePssDevListDevices", CharSet=CharSet.Ansi)]
	    public static extern int ListDevices([In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex=0, SizeConst=8)] ScePsmDevDevice[] deviceArray);
		
	    [DllImport(NATIVE_DLL, EntryPoint="scePssDevSetConsoleWrite")]
	    public static extern ScePsmDevErrorCode SetConsoleCallback(PsmDeviceConsoleCallback proc);
		
	    [DllImport(NATIVE_DLL, EntryPoint="scePssDevUninstall", CharSet=CharSet.Ansi)]
	    public static extern ScePsmDevErrorCode Uninstall(int handle, string appId);
	}

	public class NativeFunctions99
	{
		[DllImport("psm_device64.dll", EntryPoint="scePsmDevConnect")]
		public static extern int Connect (ref Guid deviceGuid);

		[DllImport("psm_device64.dll", EntryPoint="scePsmDevCreatePackage", CharSet=CharSet.Ansi)]
		public static extern ScePsmDevErrorCode CreatePackage (string packageFile, string dirForPack);

		[DllImport("psm_device64.dll", EntryPoint="scePsmDevDisconnect")]
		public static extern ScePsmDevErrorCode Disconnect (int handle);

		[DllImport("psm_device64.dll", EntryPoint="scePsmDevGetErrStr")]
		public static extern int GetErrStr (StringBuilder str);

		[DllImport("psm_device64.dll", EntryPoint="scePsmDevInstall", CharSet=CharSet.Ansi)]
		public static extern ScePsmDevErrorCode Install (int handle, string packageFile, string appId);

		[DllImport("psm_device64.dll", EntryPoint="scePsmDevKill")]
		public static extern ScePsmDevErrorCode Kill (int handle);

		[DllImport("psm_device64.dll", EntryPoint="scePsmDevLaunch", CharSet=CharSet.Ansi)]
		public static extern ScePsmDevErrorCode Launch (int handle, string appId, bool debug, bool profile, bool keepnet, string arg);

		[DllImport("psm_device64.dll", EntryPoint="scePsmDevListDevices", CharSet=CharSet.Ansi)]
		public static extern int ListDevices ([In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex=0, SizeConst=8)] ScePsmDevDevice[] deviceArray);

		[DllImport("psm_device64.dll", EntryPoint="scePsmDevSetConsoleWrite")]
		public static extern ScePsmDevErrorCode SetConsoleCallback (PsmDeviceConsoleCallback proc);

		[DllImport("psm_device64.dll", EntryPoint="scePsmDevUninstall", CharSet=CharSet.Ansi)]
		public static extern ScePsmDevErrorCode Uninstall (int handle, string appId);
	}
}
