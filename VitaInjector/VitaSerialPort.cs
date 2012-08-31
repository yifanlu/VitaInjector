using System;
using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace VitaInjector
{
	public class VitaSerialPort98 : IDisposable
	{
		// Fields
		private const uint DTR_CONTROL_DISABLE = 0;
		private const uint DTR_CONTROL_ENABLE = 1;
		private const uint FILE_FLAG_NORMAL = 0x80;
		private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
		private const uint GENERIC_READ = 0x80000000;
		private const uint GENERIC_WRITE = 0x40000000;
		private SafeFileHandle handle;
		private const uint OPEN_EXISTING = 3;
		private string portName;

		// Methods
		public VitaSerialPort98 (string portName)
		{
			this.portName = portName;
		}

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool ClearCommError (SafeFileHandle hFile, [Optional] out CommError lpErrors, [Optional] out COMSTAT lpStat);

		public void Close ()
		{
			this.handle.Close ();
		}

		[DllImport("kernel32.dll", CharSet=CharSet.Auto, SetLastError=true)]
		private static extern SafeFileHandle CreateFile (string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

		public void Dispose ()
		{
			this.handle.Dispose ();
		}

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool GetCommMask (SafeFileHandle hFile, out CommEvent lpEvtMask);

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool GetCommModemStatus (SafeFileHandle hFile, out ModemStatus lpModemStat);

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool GetCommState (SafeFileHandle hFile, out DCB lpDCB);

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool GetCommTimeouts (SafeFileHandle hFile, out COMMTIMEOUTS lpCommTimeouts);

		private void GetDcb (out DCB dcb)
		{
			if (!GetCommState (this.handle, out dcb)) {
				this.ThrowWin32Error ();
			}
		}

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern uint GetFileType (SafeFileHandle handle);

		public CommEvent GetNextEvent ()
		{
			CommEvent event2;
			if (!WaitCommEvent (this.handle, out event2, IntPtr.Zero)) {
				this.ThrowWin32Error ();
			}
			return event2;
		}

		public void Open ()
		{
			this.handle = CreateFile (@"\\.\" + this.portName, 0xc0000000, 0, IntPtr.Zero, 3, 0x80, IntPtr.Zero);
			if (this.handle.IsInvalid) {
				this.ThrowWin32Error ();
			}
		}

		public unsafe int Read (byte[] buf, int offset, int len)
		{
			if (((offset < 0) || (len < 0)) || ((len + offset) > buf.Length)) {
				throw new ArgumentException ();
			}
			fixed (byte* numRef = buf) {
				uint num;
				if (!ReadFile (this.handle, (IntPtr)(numRef + offset), (uint)len, out num, IntPtr.Zero)) {
					if (!this.IsOpen) {
						return 0;
					}
					this.ThrowWin32Error ();
				}
				return (int)num;
			}
		}

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool ReadFile (SafeFileHandle hFile, IntPtr lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, IntPtr lpOverlapped);

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool SetCommMask (SafeFileHandle hFile, CommEvent dwEvtMask);

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool SetCommState (SafeFileHandle hFile, [In] ref DCB lpDCB);

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool SetCommTimeouts (SafeFileHandle hFile, ref COMMTIMEOUTS lpCommTimeouts);

		private void SetDcb (ref DCB dcb)
		{
			if (!SetCommState (this.handle, ref dcb)) {
				this.ThrowWin32Error ();
			}
		}

		private void ThrowWin32Error ()
		{
			int hresult = Marshal.GetHRForLastWin32Error ();
			if (hresult == -2147024894) {
				this.Dispose ();
				throw new IOException ("Could not open port '" + this.portName + "'", hresult);
			}
			Marshal.ThrowExceptionForHR (hresult);
		}

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool WaitCommEvent (SafeFileHandle hFile, out CommEvent lpEvtMask, ref NativeOverlapped lpOverlapped);

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool WaitCommEvent (SafeFileHandle hFile, out CommEvent lpEvtMask, IntPtr lpOverlapped);

		public unsafe void Write (byte[] buf, int offset, int len)
		{
			if (((offset < 0) || (len < 0)) || ((len + offset) > buf.Length)) {
				throw new ArgumentException ();
			}
			fixed (byte* numRef = buf) {
				while (len > 0) {
					uint num;
					if (!WriteFile (this.handle, (IntPtr)(numRef + offset), (uint)len, out num, IntPtr.Zero)) {
						this.ThrowWin32Error ();
					}
					if (len == num) {
						return;
					}
					len -= (int)num;
				}
			}
		}

		[DllImport("kernel32.dll", SetLastError=true)]
		private static extern bool WriteFile (SafeFileHandle hFile, IntPtr lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

		// Properties
		public uint BytesToRead {
			get {
				CommError error;
				COMSTAT comstat;
				if (!ClearCommError (this.handle, out error, out comstat)) {
					this.ThrowWin32Error ();
				}
				if (error != ((CommError)0)) {
					throw new Exception ("Comm error: " + error);
				}
				return comstat.cbInQue;
			}
		}

		public CommEvent CommMask {
			get {
				CommEvent event2;
				if (!GetCommMask (this.handle, out event2)) {
					this.ThrowWin32Error ();
				}
				return event2;
			}
			set {
				if (!SetCommMask (this.handle, value)) {
					this.ThrowWin32Error ();
				}
			}
		}

		public bool DtrEnable {
			get {
				DCB dcb;
				this.GetDcb (out dcb);
				return (dcb.DtrControl == DtrControl.Enable);
			}
			set {
				DCB dcb;
				this.GetDcb (out dcb);
				dcb.DtrControl = DtrControl.Enable;
				this.SetDcb (ref dcb);
			}
		}

		public bool IsOpen {
			get {
				return !this.handle.IsClosed;
			}
		}

		public bool RtsEnable {
			get {
				DCB dcb;
				this.GetDcb (out dcb);
				return (dcb.RtsControl == RtsControl.Enable);
			}
			set {
				DCB dcb;
				this.GetDcb (out dcb);
				dcb.RtsControl = RtsControl.Enable;
				this.SetDcb (ref dcb);
			}
		}

		public ModemStatus Status {
			get {
				ModemStatus status;
				if (!GetCommModemStatus (this.handle, out status)) {
					this.ThrowWin32Error ();
				}
				return status;
			}
		}

		// Nested Types
		private enum CommError : uint
		{
			Break = 0x10,
			Frame = 8,
			Overrun = 2,
			RxOver = 1,
			RxParity = 4
		}

		[Flags]
    public enum CommEvent : uint
		{
			All = 0x1ff,
			Break = 0x40,
			Cts = 8,
			Dsr = 0x10,
			Err = 0x80,
			Ring = 0x100,
			Rlsd = 0x20,
			RxChar = 1,
			RxFlag = 2,
			TxEmpty = 4
		}

		[StructLayout(LayoutKind.Sequential)]
    private struct COMMTIMEOUTS
		{
			private uint ReadIntervalTimeout;
			private uint ReadTotalTimeoutMultiplier;
			private uint ReadTotalTimeoutConstant;
			private uint WriteTotalTimeoutMultiplier;
			private uint WriteTotalTimeoutConstant;
		}

		[StructLayout(LayoutKind.Sequential)]
    private struct COMSTAT
		{
			private static int fCtsHold;
			private static int fDsrHold;
			private static int fRlsdHold;
			private static int fXoffHold;
			private static int fXoffSent;
			private static int fEof;
			private static int fTxim;
			private BitVector32 flags;
			public uint cbInQue;
			public uint cbOutQue;

			static COMSTAT ()
			{
				fCtsHold = BitVector32.CreateMask ();
				fDsrHold = BitVector32.CreateMask (fCtsHold);
				fRlsdHold = BitVector32.CreateMask (fDsrHold);
				fXoffHold = BitVector32.CreateMask (fRlsdHold);
				fXoffSent = BitVector32.CreateMask (fXoffHold);
				fEof = BitVector32.CreateMask (fXoffSent);
				fTxim = BitVector32.CreateMask (fEof);
			}

			public bool CtsHold {
				get {
					return this.flags [fCtsHold];
				}
			}

			public bool DsrHold {
				get {
					return this.flags [fDsrHold];
				}
			}

			public bool RlsdHold {
				get {
					return this.flags [fRlsdHold];
				}
			}

			public bool XoffHold {
				get {
					return this.flags [fXoffHold];
				}
			}

			public bool XoffSent {
				get {
					return this.flags [fXoffSent];
				}
			}

			public bool Eof {
				get {
					return this.flags [fEof];
				}
			}

			public bool Txim {
				get {
					return this.flags [fTxim];
				}
			}
		}

		[StructLayout(LayoutKind.Sequential)]
    private struct DCB
		{
			internal uint DCBLength;
			internal uint BaudRate;
			private BitVector32 flags;
			private ushort wReserved;
			internal ushort XonLim;
			internal ushort XoffLim;
			internal byte ByteSize;
			internal VitaSerialPort98.Parity Parity;
			internal VitaSerialPort98.StopBits StopBits;
			internal byte XonChar;
			internal byte XoffChar;
			internal byte ErrorChar;
			internal byte EofChar;
			internal byte EvtChar;
			private ushort wReserved1;
			private static readonly int fBinary;
			private static readonly int fParity;
			private static readonly int fOutxCtsFlow;
			private static readonly int fOutxDsrFlow;
			private static readonly BitVector32.Section fDtrControl;
			private static readonly int fDsrSensitivity;
			private static readonly int fTXContinueOnXoff;
			private static readonly int fOutX;
			private static readonly int fInX;
			private static readonly int fErrorChar;
			private static readonly int fNull;
			private static readonly BitVector32.Section fRtsControl;
			private static readonly int fAbortOnError;

			static DCB ()
			{
				fBinary = BitVector32.CreateMask ();
				fParity = BitVector32.CreateMask (fBinary);
				fOutxCtsFlow = BitVector32.CreateMask (fParity);
				fOutxDsrFlow = BitVector32.CreateMask (fOutxCtsFlow);
				fDsrSensitivity = BitVector32.CreateMask (BitVector32.CreateMask (BitVector32.CreateMask (fOutxDsrFlow)));
				fTXContinueOnXoff = BitVector32.CreateMask (fDsrSensitivity);
				fOutX = BitVector32.CreateMask (fTXContinueOnXoff);
				fInX = BitVector32.CreateMask (fOutX);
				fErrorChar = BitVector32.CreateMask (fInX);
				fNull = BitVector32.CreateMask (fErrorChar);
				fAbortOnError = BitVector32.CreateMask (BitVector32.CreateMask (BitVector32.CreateMask (fNull)));
				BitVector32.Section previous = BitVector32.CreateSection (1);
				previous = BitVector32.CreateSection (1, previous);
				previous = BitVector32.CreateSection (1, previous);
				previous = BitVector32.CreateSection (1, previous);
				fDtrControl = BitVector32.CreateSection (2, previous);
				previous = BitVector32.CreateSection (1, fDtrControl);
				previous = BitVector32.CreateSection (1, previous);
				previous = BitVector32.CreateSection (1, previous);
				previous = BitVector32.CreateSection (1, previous);
				previous = BitVector32.CreateSection (1, previous);
				previous = BitVector32.CreateSection (1, previous);
				fRtsControl = BitVector32.CreateSection (3, previous);
				previous = BitVector32.CreateSection (1, fRtsControl);
			}

			public bool Binary {
				get {
					return this.flags [fBinary];
				}
				set {
					this.flags [fBinary] = value;
				}
			}

			public bool CheckParity {
				get {
					return this.flags [fParity];
				}
				set {
					this.flags [fParity] = value;
				}
			}

			public bool OutxCtsFlow {
				get {
					return this.flags [fOutxCtsFlow];
				}
				set {
					this.flags [fOutxCtsFlow] = value;
				}
			}

			public bool OutxDsrFlow {
				get {
					return this.flags [fOutxDsrFlow];
				}
				set {
					this.flags [fOutxDsrFlow] = value;
				}
			}

			public VitaSerialPort98.DtrControl DtrControl {
				get {
					return (VitaSerialPort98.DtrControl)this.flags [fDtrControl];
				}
				set {
					this.flags [fDtrControl] = (int)value;
				}
			}

			public bool DsrSensitivity {
				get {
					return this.flags [fDsrSensitivity];
				}
				set {
					this.flags [fDsrSensitivity] = value;
				}
			}

			public bool TxContinueOnXoff {
				get {
					return this.flags [fTXContinueOnXoff];
				}
				set {
					this.flags [fTXContinueOnXoff] = value;
				}
			}

			public bool OutX {
				get {
					return this.flags [fOutX];
				}
				set {
					this.flags [fOutX] = value;
				}
			}

			public bool InX {
				get {
					return this.flags [fInX];
				}
				set {
					this.flags [fInX] = value;
				}
			}

			public bool ReplaceErrorChar {
				get {
					return this.flags [fErrorChar];
				}
				set {
					this.flags [fErrorChar] = value;
				}
			}

			public bool Null {
				get {
					return this.flags [fNull];
				}
				set {
					this.flags [fNull] = value;
				}
			}

			public VitaSerialPort98.RtsControl RtsControl {
				get {
					return (VitaSerialPort98.RtsControl)this.flags [fRtsControl];
				}
				set {
					this.flags [fRtsControl] = (int)value;
				}
			}

			public bool AbortOnError {
				get {
					return this.flags [fAbortOnError];
				}
				set {
					this.flags [fAbortOnError] = value;
				}
			}
		}

		private enum DtrControl
		{
			Disable,
			Enable,
			Handshake
		}

		[Flags]
    public enum ModemStatus : uint
		{
			Cts = 0x10,
			Dsr = 0x20,
			Ring = 0x40,
			Rlsd = 0x80
		}

		private enum Parity : byte
		{
			Even = 2,
			Mark = 3,
			None = 0,
			Odd = 1,
			Space = 4
		}

		private enum RtsControl
		{
			Disable,
			Enable,
			Handshake,
			Toggle
		}

		private enum StopBits : byte
		{
			One = 0,
			OnePointFive = 1,
			Two = 2
		}
	}
}

