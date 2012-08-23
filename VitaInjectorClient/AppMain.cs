using System;
using System.Collections.Generic;

using Sce.PlayStation.Core;
using Sce.PlayStation.Core.Environment;
using Sce.PlayStation.Core.Graphics;
using Sce.PlayStation.Core.Input;
using Sce.PlayStation.HighLevel.UI;

namespace VitaInjectorClient
{
	public delegate int RunCode(int param);
	public delegate IntPtr pss_code_mem_alloc(IntPtr length);
	public delegate void pss_code_mem_unlock();
	
	public class AppMain
	{
		private static GraphicsContext graphics;
		private static Label label;
		private static bool connected = false;
		// helper fields for memory dumping
		public static readonly int BLOCK_SIZE = 0x100;
		public static IntPtr src = new IntPtr(0);
		public static byte[] dest = new byte[BLOCK_SIZE];
		
		public static void Connect ()
		{
			connected = true;
			label.Text = "Connected.";
		}
		
		public static void Main (string[] args)
		{
			Initialize ();

			while (true) {
				SystemEvents.CheckEvents ();
				Update ();
				Render ();
			}
		}

		public static void Initialize ()
		{
			// Set up the graphics system
			graphics = new GraphicsContext ();
			
			UISystem.Initialize (graphics);
			Scene myScene = new Scene();
			label = new Label();
			label.X = 10.0f;
			label.Y = 10.0f;
			label.Width = 300.0f;
			label.Text = "Waiting for connection...";
			myScene.RootWidget.AddChildLast(label);
			
			UISystem.SetScene(myScene, null);
		}

		public static void Update ()
		{
			List<TouchData> touchDataList = Touch.GetData (0);
			UISystem.Update(touchDataList);
		}

		public static void Render ()
		{
			// Clear the screen
			graphics.SetClearColor (0.0f, 0.0f, 0.0f, 0.0f);
			graphics.Clear ();
			
			UISystem.Render ();

			// Present the screen
			graphics.SwapBuffers ();
		}
	}
}
