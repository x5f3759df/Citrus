#if MONOMAC || MAC
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using Foundation;
using Lime.Platform;
#if MAC
using AppKit;
using CoreGraphics;
using OpenTK.Graphics;
#else
using MonoMac.AppKit;
using MonoMac.CoreGraphics;
#endif

namespace Lime
{
	public class Window : CommonWindow, IWindow
	{
		private NSWindow window;
		private FPSCounter fpsCounter;
		private Stopwatch stopwatch;
		private bool invalidated;
		private float refreshRate;
		private bool dialogMode;
		private Display display;
		private bool closed;

		public NSGameView View { get; private set; }

		public string Title
		{
			get { return window.Title; }
			set { window.Title = value; }
		}

		public bool Visible
		{
			get { return !View.Hidden; }
			set { SetVisible(value, dialogMode: false); }
		}

		public Vector2 ClientPosition
		{
			get { return DecoratedPosition - new Vector2(0, titleBarHeight); }
			set { DecoratedPosition = value + new Vector2(0, titleBarHeight); }
		}

		public Vector2 ClientSize
		{
			get { return new Vector2((float)View.Bounds.Width, (float)View.Bounds.Height); }
			set { window.SetContentSize(new CGSize(value.X.Round(), value.Y.Round())); }
		}

		public float PixelScale
		{
			get { return (float)window.BackingScaleFactor; }
		}

		public Vector2 DecoratedPosition
		{
			get { return new Vector2((float)window.Frame.X, (float)window.Frame.Y); }
			set
			{
				var frame = window.Frame;
				frame.Location = new CGPoint(value.X.Round(), value.Y.Round());
				window.SetFrame(frame, true);
			}
		}

		public Vector2 DecoratedSize
		{
			get { return new Vector2((float)window.Frame.Width, (float)window.Frame.Height); }
			set
			{
				var frame = window.Frame;
				frame.Size = new CGSize(value.X.Round(), value.Y.Round());
				window.SetFrame(frame, true);
			}
		}

		public Vector2 MinimumDecoratedSize
		{
			get { return new Vector2((float)window.MinSize.Width, (float)window.MinSize.Height); }
			set { window.MinSize = new CGSize(value.X.Round(), value.Y.Round()); }
		}

		public Vector2 MaximumDecoratedSize
		{
			get { return new Vector2((float)window.MaxSize.Width, (float)window.MaxSize.Height); }
			set { window.MaxSize = new CGSize(value.X.Round(), value.Y.Round()); }
		}

		public bool Active
		{
			get { return window.IsKeyWindow; }
		}

		public WindowState State
		{
			get
			{
				if (window.IsMiniaturized) {
					return WindowState.Minimized;
				}
				if ((window.StyleMask & NSWindowStyle.FullScreenWindow) != 0) {
					return WindowState.Fullscreen;
				}
				return WindowState.Normal;
			}
			set
			{
				if (State == value) {
					return;
				}
				if (value == WindowState.Minimized) {
					window.Miniaturize(null);
					return;
				}
				if (value == WindowState.Fullscreen && State != WindowState.Fullscreen) {
					window.ToggleFullScreen(null);
				} else if (value != WindowState.Fullscreen && State == WindowState.Fullscreen) {
					window.ToggleFullScreen(null);
				}
			}
		}

		public bool Fullscreen
		{
			get { return State == WindowState.Fullscreen; }
			set
			{
				if (value && State == WindowState.Fullscreen || !value && State != WindowState.Fullscreen) {
					return;
				}
				State = value ? WindowState.Fullscreen : WindowState.Normal;
			}
		}

		public NSGameView NSGameView { get { return View; } }

		private MouseCursor cursor = MouseCursor.Default;
		public MouseCursor Cursor
		{
			get { return cursor; }
			set
			{
				if (cursor != value) {
					cursor = value;
					value.NativeCursor.Set();
				}
			}
		}

		public bool AllowDropFiles
		{
			get { return View.AllowDropFiles; }
			set { View.AllowDropFiles = value; }
		}

		public event Action<IEnumerable<string>> FilesDropped;

		public float FPS { get { return fpsCounter.FPS; } }

		[Obsolete("Use FPS property instead", true)]
		public float CalcFPS() { return fpsCounter.FPS; }

		public Input Input { get; private set; }

		public Window(WindowOptions options)
		{
			this.refreshRate = options.RefreshRate;
			Input = new Input();
			fpsCounter = new FPSCounter();
			CreateNativeWindow(options);
			if (Application.MainWindow == null) {
				Application.MainWindow = this;
			}
			Application.Windows.Add(this);
			ClientSize = options.ClientSize;
			Title = options.Title;
			if (options.Visible) {
				Visible = true;
			}
			if (options.Centered) {
				Center();
			}
			stopwatch = new Stopwatch();
			stopwatch.Start();
		}

		private Vector2 windowedClientSize;
		private float titleBarHeight;
		private bool shouldFixFullscreen;

		public IDisplay Display
		{
			get
			{
				if (display == null || window.Screen != display.NativeScreen) {
					display = new Display(window.Screen);
				}
				return display;
			}
		}

		private void CreateNativeWindow(WindowOptions options)
		{
			var rect = new CGRect(0, 0, options.ClientSize.X, options.ClientSize.Y);
			View = new NSGameView(Input, rect, Platform.GraphicsMode.Default);
			NSWindowStyle style;
			if (options.Style == WindowStyle.Borderless) {
				style = NSWindowStyle.Borderless;
			} else if (options.Style == WindowStyle.Dialog) {
				style = NSWindowStyle.Titled;
			} else {
				style = NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Miniaturizable;
			}
			if (!options.FixedSize) {
				style |= NSWindowStyle.Resizable;
			}
			window = new NSWindow(rect, style, NSBackingStore.Buffered, false);

			var contentRect = window.ContentRectFor(rect);
			titleBarHeight = ((RectangleF)rect).Height - (float)contentRect.Height;

			if (options.MinimumDecoratedSize != Vector2.Zero) {
				MinimumDecoratedSize = options.MinimumDecoratedSize;
			}
			if (options.MaximumDecoratedSize != Vector2.Zero) {
				MaximumDecoratedSize = options.MaximumDecoratedSize;
			}
			window.Title = options.Title;
			window.WindowShouldClose += OnShouldClose;
			window.WillClose += OnWillClose;
			window.DidResize += (s, e) => {
				View.UpdateGLContext();
				HandleResize(s, e);
			};
			window.WillEnterFullScreen += (sender, e) => {
				shouldFixFullscreen = !window.StyleMask.HasFlag(NSWindowStyle.Resizable);
				if (shouldFixFullscreen) {
					window.StyleMask |= NSWindowStyle.Resizable;
				}
				windowedClientSize = ClientSize;
			};
			window.WillExitFullScreen += (sender, e) => {
				ClientSize = windowedClientSize;
			};
			window.DidExitFullScreen += (sender, e) => {
				if (shouldFixFullscreen) {
					window.StyleMask &= ~NSWindowStyle.Resizable;
				}
			};
			window.DidBecomeKey += (sender, e) => {
				RaiseActivated();
			};
			window.DidResignKey += (sender, e) => {
				Input.ClearKeyState();
				RaiseDeactivated();
			};
#if MAC
			window.DidMove += HandleMove;
#else
			window.DidMoved += HandleMove;
#endif
			window.CollectionBehavior = NSWindowCollectionBehavior.FullScreenPrimary;
			window.ContentView = View;
			window.ReleasedWhenClosed = true;
			View.Update += Update;
			View.RenderFrame += HandleRenderFrame;
			View.FilesDropped += RaiseFilesDropped;
		}

		private bool OnShouldClose(NSObject sender)
		{
			if (Application.MainWindow != this) {
				return RaiseClosing();
			}
			var cancel = OtherWindows.Any(w => w.RaiseClosing());
			return RaiseClosing() || cancel;
		}

		private void OnWillClose(object sender, EventArgs args)
		{
			if (Application.MainWindow == this) {
				CloseMainWindow();
			}
			else {
				CloseWindow();
			}
		}

		private void CloseMainWindow()
		{
			if (closed) {
				return;
			}
			foreach (var window in OtherWindows) {
				window.CloseWindow();
			}
			CloseWindow();
			NSApplication.SharedApplication.Terminate(View);
			TexturePool.Instance.DiscardAllTextures();
			AudioSystem.Terminate();
		}

		private void CloseWindow()
		{
			if (closed) {
				return;
			}
			RaiseClosed();
			View.Stop();
			Application.Windows.Remove(this);
			closed = true;
		}

		// Reverse by convention - Window.Win behave like this.
		private IEnumerable<Window> OtherWindows =>
			Application.Windows
				.Where(w => w != this)
				.Cast<Window>()
				.Reverse();

		public void Invalidate()
		{
			invalidated = true;
		}

		public void Center()
		{
			var displayBounds = window.Screen.VisibleFrame;
			DecoratedPosition = new Vector2 {
				X = (int)Math.Max(0, (displayBounds.Width - DecoratedSize.X) / 2 + displayBounds.Left),
				Y = (int)Math.Max(0, (displayBounds.Height - DecoratedSize.Y) / 2 + displayBounds.Top)
			};
		}

		public void Close()
		{
			window.Close();
			if (dialogMode) {
				NSApplication.SharedApplication.StopModal();
			}
		}

		public void ShowModal()
		{
			if (Visible) {
				throw new InvalidOperationException();
			}
			SetVisible(true, dialogMode: true);
		}

		private void HandleRenderFrame()
		{
			if (invalidated) {
				fpsCounter.Refresh();
				View.MakeCurrent();
				RaiseRendering();
				View.SwapBuffers();
				invalidated = false;
			}
		}

		private void HandleResize(object sender, EventArgs e)
		{
			RaiseResized(deviceRotated: false);
			Invalidate();
		}

		private void HandleMove(object sender, EventArgs e)
		{
			RaiseMoved();
		}

		private void Update()
		{
			var delta = (float)stopwatch.Elapsed.TotalSeconds;
			stopwatch.Restart();
			delta = Mathf.Clamp(delta, 0, 1 / Application.LowFPSLimit);
			// Refresh mouse position on every frame to make HitTest work properly if mouse is outside of the window.
			RefreshMousePosition();
			Input.ProcessPendingKeyEvents(delta);
			RaiseUpdating(delta);
			AudioSystem.Update();
			Input.TextInput = null;
			Input.CopyKeysState();
		}

		private void RefreshMousePosition()
		{
			var p = window.MouseLocationOutsideOfEventStream;
			Input.MousePosition = new Vector2((float)p.X, (float)(NSGameView.Frame.Height - p.Y)) * Input.ScreenToWorldTransform;
		}

		private void SetVisible(bool value, bool dialogMode)
		{
			RaiseVisibleChanging(value);
			this.dialogMode = dialogMode;
			View.Hidden = !value;
			View.Stop();
			if (value) {
				View.Run(refreshRate, dialogMode);
				window.MakeKeyAndOrderFront(window);
			}
			if (dialogMode) {
				// Showing a new modal window should consume issued commands or we may fall into infinite loop otherwise.
				Application.UpdateCounter++;
				NSApplication.SharedApplication.RunModalForWindow(window);
			}
		}

		private void RaiseFilesDropped(IEnumerable<string> files)
		{
			using (Context.Activate().Scoped()) {
				FilesDropped?.Invoke(files);
			}
		}
	}
}
#endif
