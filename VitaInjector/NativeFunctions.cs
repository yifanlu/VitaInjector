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
    public unsafe struct ScePsmDevice
	{
        public Guid guid;
        public PsmDeviceType type;
        public int online;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x80)]
        public char[] deviceID;

    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ScePsmApplication
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x80)]
        public char[] name;
        public int size;
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
        public static extern int ListDevices([In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0, SizeConst = 8)] ScePsmDevice[] deviceArray);
		
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
        public static extern int ListDevices([In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0, SizeConst = 8)] ScePsmDevice[] deviceArray);

		[DllImport("psm_device64.dll", EntryPoint="scePsmDevSetConsoleWrite")]
		public static extern ScePsmDevErrorCode SetConsoleCallback (PsmDeviceConsoleCallback proc);

		[DllImport("psm_device64.dll", EntryPoint="scePsmDevUninstall", CharSet=CharSet.Ansi)]
		public static extern ScePsmDevErrorCode Uninstall (int handle, string appId);
	}

    public class NativeFunctions100
    {
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevConnect")]
        public static extern int Connect([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevCreatePackage")]
        public static extern int CreatePackage([MarshalAs(UnmanagedType.LPStr)] string packageFile, [MarshalAs(UnmanagedType.LPStr)] string dirForPack);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevDisconnect")]
        public static extern int Disconnect([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevExistAppExeKey")]
        public static extern int ExistAppExeKey([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid, long accountId, [MarshalAs(UnmanagedType.LPStr)] string titleIdentifier, [MarshalAs(UnmanagedType.LPStr)] string env);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevExtractPackage")]
        public static extern int ExtractPackage([MarshalAs(UnmanagedType.LPStr)] string dirExtract, [MarshalAs(UnmanagedType.LPStr)] string packageFile);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevGetDeviceSeed")]
        public static extern int GetDeviceSeed([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid, [MarshalAs(UnmanagedType.LPStr)] string filename);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevGetErrStr")]
        public static extern int GetErrStr([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid, [In, Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder errstr);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevGetLog")]
        public static extern int GetLog([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid, [In, Out, MarshalAs(UnmanagedType.LPStr)] StringBuilder logstr);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevInstall")]
        public static extern int Install([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid, [MarshalAs(UnmanagedType.LPStr)] string packageFile, [MarshalAs(UnmanagedType.LPStr)] string appId);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevKill")]
        public static extern int Kill([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevLaunch")]
        public static extern int Launch([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid, [MarshalAs(UnmanagedType.LPStr)] string appId, bool debug, bool profile, bool keepnet, [MarshalAs(UnmanagedType.LPStr)] string arg);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevListApplications")]
        public static extern int ListApplications([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid, [In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0, SizeConst = 100)] ScePsmApplication[] appArray);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevListDevices")]
        public static extern int ListDevices([In, Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0, SizeConst = 8)] ScePsmDevice[] deviceArray);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevPickFileFromPackage")]
        public static extern int PickFileFromPackage([MarshalAs(UnmanagedType.LPStr)] string outName, [MarshalAs(UnmanagedType.LPStr)] string packageFile, [MarshalAs(UnmanagedType.LPStr)] string inName);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevSetAdbExePath", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern int SetAdbExePath(string path);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevSetAppExeKey")]
        public static extern int SetAppExeKey([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid, [MarshalAs(UnmanagedType.LPStr)] string filename);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevSetConsoleWrite")]
        public static extern int SetConsoleWrite([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid, IntPtr proc);
        [DllImport(@"psm_device64.dll", EntryPoint = "scePsmDevUninstall")]
        public static extern int Uninstall([In, MarshalAs(UnmanagedType.LPStruct)] Guid deviceGuid, [MarshalAs(UnmanagedType.LPStr)] string appId);
    }
}
