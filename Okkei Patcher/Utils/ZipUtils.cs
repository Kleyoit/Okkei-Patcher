using ICSharpCode.SharpZipLib.Zip;

namespace OkkeiPatcher.Utils
{
	internal static class ZipUtils
	{
		public static void UpdateZip(ZipFile zipFile)
		{
			zipFile.CommitUpdate();
			zipFile.Close();
		}

		public static void RemoveApkSignature(ZipFile zipFile)
		{
			foreach (ZipEntry ze in zipFile)
				if (ze.Name.StartsWith("META-INF/"))
					zipFile.Delete(ze);
		}

		public static void ExtractZip(string zipPath, string extractPath)
		{
			var fastZip = new FastZip();
			const string fileFilter = null;
			fastZip.ExtractZip(zipPath, extractPath, fileFilter);
		}
	}
}