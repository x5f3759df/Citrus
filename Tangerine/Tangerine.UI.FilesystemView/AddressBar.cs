using Lime;
using System.Collections.Generic;
using System.Linq;

namespace Tangerine.UI.FilesystemView
{
	public class AddressBar : Toolbar
	{
		public enum AddressBarState
		{
			PathBar,
			Editor
		}
		private string buffer = "C:\\";
		private AddressBarState state;
		private PathBar pathBar;
		private ThemedEditBox editor;
		private FilesystemView view;

		public string Path
		{
			get {
				return buffer;
			}
			set {
				buffer = AdjustPath(value);
			}
		}

		public AddressBar(FilesystemView view, Model model)
		{
			this.view = view;
			Layout = new StackLayout();
			Path = model.CurrentPath;
			buffer = Path;
			state = AddressBarState.PathBar;
			CreatePathBar();
			CreateEditor();

			Updating += (float delta) => {
				if (
					editor.IsFocused() &&
					state != AddressBarState.Editor
				) {
					state = AddressBarState.Editor;
					editor.Text = buffer;
					DeletePathBar();
				}
				if (
					state == AddressBarState.Editor &&
					!editor.IsFocused()
				) {
					FlipState();
				}
			};
		}
		
		private IEnumerator<object> ShowAlertTask(string message)
		{
			yield return Task.WaitWhile(() => Input.ConsumeKeyPress(Key.Enter));

			var dialog = new AlertDialog(message);
			dialog.Show();
		}

		public string AdjustPath(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) {
				return buffer;
			}
			if (path.Length < 3) {
				Tasks.Add(ShowAlertTask("The size of the path is less than the permissible."));
				return buffer;
			}

			char[] charsToTrim = { '.', ' ' };
			path = path.Trim(charsToTrim);

			if (path.Contains(System.IO.Path.AltDirectorySeparatorChar.ToString())) {
				Tasks.Add(ShowAlertTask("The path is in an invalid format."));
				return buffer;
			}

			//If the user added many slashes
			string doubleDirectorySeparator = string.Empty;
			doubleDirectorySeparator += System.IO.Path.DirectorySeparatorChar;
			doubleDirectorySeparator += System.IO.Path.DirectorySeparatorChar;
			if (path.Contains(doubleDirectorySeparator)) {
				Tasks.Add(ShowAlertTask("The path is in an invalid format."));
				return buffer;
			}

			if (
				path[path.Length - 1] == System.IO.Path.DirectorySeparatorChar &&
				path[path.Length - 2] != System.IO.Path.VolumeSeparatorChar
			) {
				path = path.Remove(path.Length - 1);
			}

			return path;
		}
		
		private string PathToFolderPath(string path)
		{
			if (System.IO.Path.GetExtension(path) != string.Empty) {
				if (!System.IO.Directory.Exists(path)) {
					var i = path.Length - 1;
					var c = path[path.Length - 1];
					while (c != System.IO.Path.DirectorySeparatorChar) {
						path = path.Remove(i);
						i--;
						c = path[i];
					}
				}
			}
			if (
				path[path.Length - 1] == System.IO.Path.DirectorySeparatorChar &&
				path[path.Length - 2] != System.IO.Path.VolumeSeparatorChar
			) {
				path = path.Remove(path.Length - 1);
			}
			return path;
		}

		public void SetFocusOnEditor()
		{
			if (state != AddressBarState.Editor) {
				FlipState();
				editor.SetFocus();
			}
		}

		private void FlipState()
		{
			if (state == AddressBarState.Editor) {
				state = AddressBarState.PathBar;
				editor.Text = "";
				Nodes.Remove(editor);
				CreatePathBar();
				Nodes.Add(editor);
			} else {
				state = AddressBarState.Editor;
				DeletePathBar();
				editor.Text = buffer;
			}
		}

		private void CreateEditor()
		{
			Nodes.Add(editor = new ThemedEditBox());
			editor.LayoutCell = new LayoutCell(Alignment.LeftCenter);
			editor.Updating += (float delta) => {
				if (editor.Input.WasKeyPressed(Key.Enter)) {
					var adjustedText = AdjustPath(editor.Text);
					if (view.Open(adjustedText)) {
						buffer = PathToFolderPath(adjustedText);
						FlipState();
					} else {
						editor.Text = buffer;
					}
				}
			};
		}

		private void CreatePathBar()
		{
			Nodes.Add(pathBar = new PathBar(view, this));
			pathBar.LayoutCell = new LayoutCell(Alignment.LeftCenter);
			pathBar.Updating += UpdatingPathBar;
		}

		private void UpdatingPathBar(float delta)
		{
			if (pathBar.IsMouseOver() && pathBar.Input.WasMouseReleased(Key.Mouse0)) {
				FlipState();
			}
		}

		private void DeletePathBar()
		{
			Nodes.Remove(pathBar);
			pathBar.Updating -= UpdatingPathBar;
			pathBar = null;
		}
	}

	public class PathBar : Widget
	{
		private string buffer;
		private string[] topFoldersPaths;
		private int countOfFolders;
		private FilesystemView view;
		private PathRootButton rootButton;
		private PathFolderButton[] folderButtons;
		private PathArrowButton[] arrowButtons;
		private PathRootArrowButton rootArrowButton;

		public PathBar(FilesystemView view, AddressBar addressBar)
		{
			this.view = view;
			buffer = addressBar.Path;
			Layout = new HBoxLayout();
			LayoutCell = new LayoutCell(Alignment.LeftCenter);
			CreateButtons();

			Updating += (float delta) => {
				if (!buffer.Equals(addressBar.Path)) {
					buffer = addressBar.Path;
					UpdatePathBar();
				}
			};
		}

		private void CreateButtons()
		{
			countOfFolders = ToСountOfFolders(buffer);
			topFoldersPaths = FillTopFoldersPaths(buffer, countOfFolders);
			folderButtons = new PathFolderButton[countOfFolders];
			arrowButtons = new PathArrowButton[countOfFolders + 1];
			rootArrowButton = new PathRootArrowButton(view);

			Nodes.Add(rootArrowButton);
			Nodes.Add(rootButton = new PathRootButton(buffer, view));
			Nodes.Add(arrowButtons[0] = new PathArrowButton(System.IO.Path.GetPathRoot(buffer), view));
			for (var i = 0; i < countOfFolders; i++) {
				Nodes.Add(folderButtons[i] = new PathFolderButton(topFoldersPaths[i], view));
				Nodes.Add(arrowButtons[i + 1] = new PathArrowButton(topFoldersPaths[i], view));
			}
		}

		private void DestroyButtons()
		{
			Nodes.Remove(rootArrowButton);
			Nodes.Remove(rootButton);
			Nodes.Remove(arrowButtons[0]);
			for (var i = 0; i < countOfFolders; i++) {
				Nodes.Remove(arrowButtons[i + 1]);
				Nodes.Remove(folderButtons[i]);
			}
		}

		private void UpdatePathBar()
		{
			DestroyButtons();
			CreateButtons();
		}

		public static int ToСountOfFolders(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) {
				return -1;
			}
			var folders = 0;
			for (var i = 0; i < path.Length; i++) {
				if (
					path[i] == System.IO.Path.DirectorySeparatorChar &&
					i + 1 != path.Length
				) {
					folders++;
				}
			}
			return folders;
		}

		public static string[] FillTopFoldersPaths(string path, int countOfFolders)
		{
			if (string.IsNullOrWhiteSpace(path)) {
				return null;
			}
			var topFolders = new string[countOfFolders];
			var i = countOfFolders - 1;
			if (countOfFolders != 0) {
				topFolders[i] = path;
				i--;
				while (i != -1) {
					topFolders[i] = System.IO.Path.GetDirectoryName(topFolders[i + 1]);
					i--;
				}
			}
			return topFolders;
		}
	}

	public class PathFolderButton : ThemedButton
	{
		public PathFolderButton(string path, FilesystemView view) : base()
		{
			Text = GetNameOfFolder(path);
			MinMaxWidth = Renderer.MeasureTextLine(Text, Theme.Metrics.TextHeight, 3).X;
			Gestures.Add(new ClickGesture(0, () => view.Open(path)));
			Gestures.Add(new ClickGesture(1, () => SystemShellContextMenu.Instance.Show(path)));
		}

		public static string GetNameOfFolder(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) {
				return null;
			} else {
				int i;
				for (i = path.Length - 1; i >= 0; i--) {
					if (path[i] == System.IO.Path.DirectorySeparatorChar) {
						i++;
						break;
					}
				}
				return path.Substring(i);
			}
		}
	}

	public class PathRootButton : ThemedButton
	{
		public PathRootButton(string path, FilesystemView view) : base()
		{
			Text = GetNameOfRoot(path);
			MinMaxWidth = Renderer.MeasureTextLine(Text, Theme.Metrics.TextHeight, 4).X;
			Gestures.Add(new ClickGesture(0, () => view.Open(System.IO.Path.GetPathRoot(path))));
			Gestures.Add(new ClickGesture(1, () => SystemShellContextMenu.Instance.Show(path)));
		}

		public static string GetNameOfRoot(string path)
		{
			if (string.IsNullOrWhiteSpace(path)) {
				return null;
			} else {
				path = System.IO.Path.GetPathRoot(path);
				return path.Remove(path.Length - 1);
			}
		}
	}

	public class PathArrowButton : ThemedButton
	{
		public enum PathArrowButtonState
		{
			Collapsed,
			Expanded
		}
		private string path;
		private PathArrowButtonState state;
		private DirectoryPicker picker;
		private FilesystemView view;

		public PathArrowButton(string path, FilesystemView view) : base()
		{
			this.path = path;
			this.view = view;

			Gestures.Add(new ClickGesture(0, FlipState));
			Text = ">";
			state = PathArrowButtonState.Collapsed;
			MinMaxWidth = Renderer.MeasureTextLine(Text, Theme.Metrics.TextHeight, 5).X;
		}

		private void FlipState()
		{
			if (state == PathArrowButtonState.Collapsed) {
				Text = "v";
				state = PathArrowButtonState.Expanded;
				var pickerPosition = Window.Current.LocalToDesktop(GlobalPosition + new Vector2(0, Height));
				picker = new DirectoryPicker(pickerPosition, path, view);
				picker.Window.Deactivated += () => {
					picker.Window.Close();
					FlipState();
				};
			} else {
				Text = ">";
				state = PathArrowButtonState.Collapsed;
				picker.Window.Close();
			}
		}
	}

	public class PathRootArrowButton : ThemedButton
	{
		public enum PathArrowButtonState
		{
			Collapsed,
			Expanded
		}
		private PathArrowButtonState state;
		private RootsDirectoryPicker picker;
		private FilesystemView view;

		public PathRootArrowButton(FilesystemView view) : base()
		{
			this.view = view;

			Gestures.Add(new ClickGesture(0, FlipState));
			Text = ">";
			state = PathArrowButtonState.Collapsed;
			MinMaxWidth = Renderer.MeasureTextLine(Text, Theme.Metrics.TextHeight, 5).X;
		}

		private void FlipState()
		{
			if (state == PathArrowButtonState.Collapsed) {
				Text = "v";
				state = PathArrowButtonState.Expanded;
				var pickerPosition = Window.Current.LocalToDesktop(GlobalPosition + new Vector2(0, Height));
				picker = new RootsDirectoryPicker(pickerPosition, view);
				picker.Window.Deactivated += () => {
					picker.Window.Close();
					FlipState();
				};
			} else {
				Text = ">";
				state = PathArrowButtonState.Collapsed;
				picker.Window.Close();
			}
		}
	}

	public class RootsDirectoryPicker : Window
	{
		private string[] internalRoots;
		private FilesystemView view;
		private bool IsMouseInside = false;
		private ThemedScrollView scrollView;

		public Window Window { get; }

		private static WindowOptions DefaultWindowOptions => new WindowOptions {
			Style = WindowStyle.Borderless,
			Centered = false,
			Visible = false
		};

		public RootsDirectoryPicker(Vector2 globalPosition, FilesystemView view) : base(DefaultWindowOptions)
		{
			this.view = view;
			var logicalDrives = System.IO.Directory.GetLogicalDrives();
			internalRoots = GetRealRootsPathsFromLogicalDrives(logicalDrives);
			var rootItems = GetRootItems(internalRoots);

			const int MaxItemsOnWindow = 19; // Like in Windows File Explorer
			var itemsCount = System.Math.Min(rootItems.Length, MaxItemsOnWindow);
			var clientSize = new Vector2(FilesystemItem.ItemWidth, (FilesystemItem.IconSize + 2 * FilesystemItem.ItemPadding) * itemsCount);
			Window = new Window(new WindowOptions {
				ClientSize = clientSize,
				FixedSize = true,
				MinimumDecoratedSize = clientSize,
				Style = WindowStyle.Borderless,
				Centered = false,
				Visible = false
			});

			scrollView = new ThemedScrollView();
			var list = new Widget {
				Layout = new VBoxLayout()
			};
			list.Nodes.AddRange(rootItems);
			scrollView.Content.Layout = new VBoxLayout { Spacing = AttachmentMetrics.Spacing };
			scrollView.Content.AddNode(list);
			var rootWidget = new ThemedInvalidableWindowWidget(Window) {
				Layout = new VBoxLayout(),
				Nodes = {
					scrollView
				}
			};

			Window.Visible = true;
			Window.ClientPosition = globalPosition;
		}

		public static string[] GetRealRootsPathsFromLogicalDrives(string[] logicalDrives)
		{
			var countOfRealRoots = 0;
			foreach(var path in logicalDrives) {
				if (System.IO.Directory.Exists(path)) {
					countOfRealRoots++;
				}
			}
			string[] realRoots = new string[countOfRealRoots];
			var i = 0;
			foreach(var root in logicalDrives) {
				if (System.IO.Directory.Exists(root)) {
					realRoots[i] = root;
					i++;
					if (i == countOfRealRoots) break;
				}
			}
			return realRoots;
		}

		private FilesystemItem[] GetRootItems(string[] paths)
		{
			FilesystemItem[] items = new FilesystemItem[internalRoots.Length];
			var i = 0;
			foreach (var path in paths) {
				FilesystemItem item = new FilesystemItem(path);
				item.CompoundPresenter.Add(new DelegatePresenter<Widget>(_ => {
					if (item.IsMouseOverThisOrDescendant()) {
						item.PrepareRendererState();
						Renderer.DrawRect(Vector2.Zero, item.Size, Theme.Colors.HoveredBackground);
					}
				}));
				item.Updating += (float delta) => {
					if (IsMouseEntering(item)) {
						Invalidate();
					}
					if (item.Input.WasMouseReleased(0)) {
						Close();
						view.Open(path);
					} else if (item.Input.WasMouseReleased(1)) {
						SystemShellContextMenu.Instance.Show(item.FilesystemPath);
					}
				};
				items[i] = item;
				i++;
			}
			return items;
		}

		private bool IsMouseEntering(Widget widget)
		{
			if (
				widget.LocalMousePosition().X >= 0 &&
				widget.LocalMousePosition().Y >= 0 &&
				widget.LocalMousePosition().X <= widget.Width &&
				widget.LocalMousePosition().Y <= widget.Height
			) {
				if (!IsMouseInside) {
					IsMouseInside = true;
					return true;
				} else {
					return false;
				}
			} else {
				IsMouseInside = false;
				return false;
			}
		}
	}

	public class DirectoryPicker
	{
		private FilesystemView view;
		private bool IsMouseInside = false;
		private ThemedScrollView scrollView;

		public Window Window { get; }

		public DirectoryPicker(Vector2 globalPosition, string path, FilesystemView view)
		{
			this.view = view;
			var internalFolders = GetInternalFoldersPaths(path);
			var filesystemItems = GetFilesystemItems(internalFolders);

			const int MaxItemsOnWindow = 19; // Like in Windows File Explorer
			var itemsCount = System.Math.Min(filesystemItems.Count, MaxItemsOnWindow);
			var clientSize = new Vector2(FilesystemItem.ItemWidth, (FilesystemItem.IconSize + 2 * FilesystemItem.ItemPadding) * itemsCount);
			Window = new Window(new WindowOptions {
				ClientSize = clientSize,
				FixedSize = true,
				MinimumDecoratedSize = clientSize,
				Style = WindowStyle.Borderless,
				Centered = false,
				Visible = false
			});

			scrollView = new ThemedScrollView();
			var list = new Widget {
				Layout = new VBoxLayout()
			};
			list.Nodes.AddRange(filesystemItems);
			scrollView.Content.Layout = new VBoxLayout { Spacing = AttachmentMetrics.Spacing };
			scrollView.Content.AddNode(list);
			var rootWidget = new ThemedInvalidableWindowWidget(Window) {
				Layout = new VBoxLayout(),
				Nodes = {
					scrollView
				}
			};

			Window.Visible = true;
			Window.ClientPosition = globalPosition;
		}

		public static List<string> GetInternalFoldersPaths(string path)
		{
			var foldersPaths = new List<string>();
			foreach (var item in System.IO.Directory.EnumerateDirectories(path).OrderBy(f => f)) {
				foldersPaths.Add(item);
			}
			return foldersPaths;
		}

		private List<FilesystemItem> GetFilesystemItems(List<string> paths)
		{
			var items = new List<FilesystemItem>();
			foreach (var path in paths) {
				FilesystemItem item;
				items.Add(item = new FilesystemItem(path));
				item.CompoundPresenter.Add(new DelegatePresenter<Widget>(_ => {
					if (item.IsMouseOverThisOrDescendant()) {
						item.PrepareRendererState();
						Renderer.DrawRect(Vector2.Zero, item.Size, Theme.Colors.HoveredBackground);
					}
				}));
				item.Updating += (float delta) => {
					if (IsMouseEntering(item)) {
						Window.Invalidate();
					}
					if (item.Input.WasMouseReleased(0)) {
						Window.Close();
						view.Open(item.FilesystemPath);
					} else if (item.Input.WasMouseReleased(1)) {
						SystemShellContextMenu.Instance.Show(item.FilesystemPath);
					}
				};
			}
			return items;
		}

		private bool IsMouseEntering(Widget widget)
		{
			if (
				widget.LocalMousePosition().X >= 0 &&
				widget.LocalMousePosition().Y >= 0 &&
				widget.LocalMousePosition().X <= widget.Width &&
				widget.LocalMousePosition().Y <= widget.Height
			) {
				if (!IsMouseInside) {
					IsMouseInside = true;
					return true;
				} else {
					return false;
				}
			} else {
				IsMouseInside = false;
				return false;
			}
		}
	}
}
