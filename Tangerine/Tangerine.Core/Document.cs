﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Lime;
using Tangerine.Core.Components;

namespace Tangerine.Core
{
	public interface IDocumentView
	{
		void Detach();
		void Attach();
	}

	public sealed class Document
	{
		public enum CloseAction
		{
			Cancel,
			SaveChanges,
			DiscardChanges
		}

		readonly string defaultPath = "Untitled";
		readonly Vector2 defaultSceneSize = new Vector2(1024, 768);

		public delegate bool PathSelectorDelegate(out string path);

		private readonly Dictionary<object, Row> rowCache = new Dictionary<object, Row>();

		public static event Action<Document> AttachingViews;
		public static Func<Document, CloseAction> Closing;
		public static PathSelectorDelegate PathSelector;

		public static Document Current { get; private set; }

		public readonly DocumentHistory History = new DocumentHistory();
		public bool IsModified => History.IsDocumentModified;

		public static string[] GetSupportedFileTypes() => new string[] { "scene", "tan" };

		/// <summary>
		/// Gets the path to the document relative to the project directory.
		/// </summary>
		public string Path { get; private set; }

		/// <summary>
		/// Gets the root node for the current document.
		/// </summary>
		public Node RootNode { get; private set; }

		/// <summary>
		/// Gets or sets the current container widget.
		/// </summary>
		public Node Container { get; set; }

		/// <summary>
		/// Gets or sets the scene we are navigated from. Need for getting back into the main scene from the external one.
		/// </summary>
		public string SceneNavigatedFrom { get; set; }

		/// <summary>
		/// The list of rows, currently displayed on the timeline.
		/// </summary>
		public readonly List<Row> Rows = new List<Row>();

		/// <summary>
		/// The root of the current row hierarchy.
		/// </summary>
		public Row RowTree { get; set; }

		/// <summary>
		/// The list of views (timeline, inspector, ...)
		/// </summary>
		public readonly List<IDocumentView> Views = new List<IDocumentView>();

		public int AnimationFrame
		{
			get { return Container.AnimationFrame; }
			set { Container.AnimationFrame = value; }
		}

		public bool PreviewAnimation { get; set; }
		public int PreviewAnimationBegin { get; set; }

		public string AnimationId { get; set; }

		public Document()
		{
			Path = defaultPath;
			Container = RootNode = new Frame { Size = defaultSceneSize };
		}

		public Document(string path)
		{
			Path = path;
			using (Theme.Push(DefaultTheme.Instance)) {
				RootNode = new Frame(path);
			}
			RootNode.Update(0);
			Container = RootNode;
		}

		public void MakeCurrent()
		{
			SetCurrent(this);
		}

		public static void SetCurrent(Document doc)
		{
			if (Current != null) {
				Current.DetachViews();
			}
			Current = doc;
			if (doc != null) {
				doc.AttachViews();
			}
		}

		void AttachViews()
		{
			AttachingViews?.Invoke(this);
			foreach (var i in Current.Views) {
				i.Attach();
			}
		}

		void DetachViews()
		{
			foreach (var i in Current.Views) {
				i.Detach();
			}
		}

		public bool Close()
		{
			if (!IsModified) {
				return true;
			}
			if (Closing != null) {
				var r = Closing(this);
				if (r == CloseAction.Cancel) {
					return false;
				}
				if (r == CloseAction.SaveChanges) {
					Save();
				}
			} else {
				Save();
			}
			return true;
		}

		public void Save()
		{
			if (Path == defaultPath) {
				string path;
				if (PathSelector(out path)) {
					SaveAs(path);
				}
			} else {
				SaveAs(Path);
			}
		}

		public void SaveAs(string path)
		{
			History.AddSavePoint();
			Path = path;
			using (var stream = new FileStream(Project.Current.GetSystemPath(path, "scene"), FileMode.Create)) {
				var serializer = new Orange.HotSceneExporter.Serializer();
				// Dispose cloned object to preserve keyframes identity in the original node. See Animator.Dispose().
				using (var node = CreateCloneForSerialization(RootNode)) {
					Serialization.WriteObject(path, stream, node, serializer);
				}
			}
		}

		public static Node CreateCloneForSerialization(Node node)
		{
			var clone = node.Clone();
			foreach (var n in clone.Descendants) {
				n.AnimationFrame = 0;
			}
			foreach (var n in clone.Descendants.Where(i => i.ContentsPath != null)) {
				n.Nodes.Clear();
				n.Markers.Clear();
			}
			return clone;
		}

		public IEnumerable<Row> SelectedRows()
		{
			foreach (var row in Rows) {
				if (row.Selected) {
					yield return row;
				}
			}
		}

		public IEnumerable<Node> SelectedNodes()
		{
			foreach (var row in Rows) {
				if (row.Selected) {
					var nr = row.Components.Get<NodeRow>();
					if (nr != null) {
						yield return nr.Node;
					}
				}
			}
		}

		public IEnumerable<IFolderItem> SelectedFolderItems()
		{
			foreach (var row in Rows) {
				if (row.Selected) {
					var nr = row.Components.Get<NodeRow>();
					if (nr != null) {
						yield return nr.Node;
					}
					var fr = row.Components.Get<FolderRow>();
					if (fr != null) {
						yield return fr.Folder;
					}
				}
			}
		}

		public IEnumerable<Row> TopLevelSelectedRows()
		{
			foreach (var row in Rows) {
				if (row.Selected) {
					var discardRow = false;
					for (var p = row.Parent; p != null; p = p.Parent) {
						discardRow |= p.Selected;
					}
					if (!discardRow) {
						yield return row;
					}
				}
			}
		}

		public Row GetRowForObject(object obj)
		{
			Row row;
			if (!rowCache.TryGetValue(obj, out row)) {
				row = new Row();
				rowCache.Add(obj, row);
			}
			return row;
		}

		public static bool HasCurrent() => Current != null;
	}
}
