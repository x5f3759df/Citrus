using System;
using System.Collections.Generic;
using System.IO;

namespace Lime
{
	[Flags]
	public enum AssetAttributes
	{
		None = 0,
		ZippedDeflate = 1 << 0,
		NonPowerOf2Texture = 1 << 1,
		ZippedLZMA = 1 << 2,
		Zipped = ZippedDeflate | ZippedLZMA
	}

	public abstract class AssetBundle : IDisposable
	{
		[ThreadStatic]
		private static AssetBundle current;

		public static AssetBundle Current
		{
			get
			{
				if (current == null) {
					throw new Lime.Exception("AssetBundle.Current must be initialized before use.");
				}
				return current;
			}
			set
			{
				current = value;
				// The game could use some of textures from this bundle, and if they are missing
				// we should notify texture pool to search them again.
				TexturePool.Instance.DiscardAllStubTextures();
			}
		}

		public static bool Initialized => current != null;

		public virtual void Dispose()
		{
			if (Current == this) {
				Current = null;
			}
		}

		public static string CurrentLanguage;

		public abstract Stream OpenFile(string path);

		public byte[] ReadFile(string path)
		{
			using (var stream = OpenFile(path)) {
				using (var memoryStream = new MemoryStream()) {
					stream.CopyTo(memoryStream);
					return memoryStream.ToArray();
				}
			}
		}

		public abstract DateTime GetFileLastWriteTime(string path);
		public abstract byte[] GetCookingRulesSHA1(string path);
		public abstract int GetFileSize(string path);

		public abstract void DeleteFile(string path);
		public abstract bool FileExists(string path);

		public abstract void ImportFile(string path, Stream stream, int reserve, string sourceExtension, AssetAttributes attributes, byte[] cookingRulesSHA1);

		public abstract IEnumerable<string> EnumerateFiles(string path = null);

		public void ImportFile(string srcPath, string dstPath, int reserve, string sourceExtension, AssetAttributes attributes, byte[] cookingRulesSHA1)
		{
			using (var stream = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
				ImportFile(dstPath, stream, reserve, sourceExtension, attributes, cookingRulesSHA1);
			}
		}

		public Stream OpenFileLocalized(string path)
		{
			var stream = OpenFile(GetLocalizedPath(path));
			return stream;
		}

		public string GetLocalizedPath(string path)
		{
			if (string.IsNullOrEmpty(CurrentLanguage))
				return path;
			string extension = Path.GetExtension(path);
			string pathWithoutExtension = Path.ChangeExtension(path, null);
			string localizedParth = pathWithoutExtension + "." + CurrentLanguage + extension;
			return FileExists(localizedParth) ? localizedParth : path;
		}

		public virtual AssetAttributes GetAttributes(string path)
		{
			return AssetAttributes.None;
		}

		public virtual void SetAttributes(string path, AssetAttributes attributes)
		{
		}
	}
}
