using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;

namespace SquadEditor.App.Ui;

/// <summary>
/// Finds logos and faces from the graphics packs FM itself uses (Documents\Sports Interactive\...\graphics).
/// Packs map image files to records through config.xml: from="file" to="graphics/pictures/club/1870/logo".
/// </summary>
public sealed class GraphicsIndex
{
	private static readonly Regex Target = new Regex(@"graphics/pictures/(?<kind>[a-z_]+)/(?:r-)?(?<id>\d+)/(?<type>[a-z_ ]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

	private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, ImageSource?> _images = new ConcurrentDictionary<string, ImageSource?>();

	public string? Root { get; private set; }

	public int ClubLogos { get; private set; }

	public int Faces { get; private set; }

	/// <summary>Documents\Sports Interactive\Football Manager 27\graphics (or the newest FM folder that has one).</summary>
	public static string? FindDefaultRoot()
	{
		string si = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive");
		if (!Directory.Exists(si))
		{
			return null;
		}
		return Directory.GetDirectories(si, "Football Manager*")
			.OrderByDescending(d => d.Contains("27") ? 1 : 0).ThenByDescending(d => d)
			.Select(d => Path.Combine(d, "graphics"))
			.FirstOrDefault(Directory.Exists);
	}

	public static GraphicsIndex Build(string? root)
	{
		var index = new GraphicsIndex { Root = root };
		if (root == null || !Directory.Exists(root))
		{
			return index;
		}
		IEnumerable<string> configs;
		try
		{
			configs = Directory.EnumerateFiles(root, "config.xml", SearchOption.AllDirectories).ToList();
		}
		catch (Exception)
		{
			return index;
		}
		foreach (string config in configs)
		{
			index.ReadConfig(config);
		}
		index.ClubLogos = index._files.Keys.Count(k => k.StartsWith("club:", StringComparison.Ordinal));
		index.Faces = index._files.Keys.Count(k => k.StartsWith("person:", StringComparison.Ordinal));
		return index;
	}

	private void ReadConfig(string config)
	{
		string dir = Path.GetDirectoryName(config)!;
		// One directory listing instead of a disk check per record (packs have tens of thousands of files).
		var present = new HashSet<string>(Directory.EnumerateFiles(dir).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
		try
		{
			using XmlReader reader = XmlReader.Create(config, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreComments = true });
			while (reader.Read())
			{
				if (reader.NodeType != XmlNodeType.Element || reader.Name != "record")
				{
					continue;
				}
				string? from = reader.GetAttribute("from");
				string? to = reader.GetAttribute("to");
				if (from == null || to == null)
				{
					continue;
				}
				Match m = Target.Match(to);
				if (!m.Success)
				{
					continue;
				}
				string kind = m.Groups["kind"].Value.ToLowerInvariant();
				string type = m.Groups["type"].Value.ToLowerInvariant();
				// Prefer the full-size logo / portrait over icons and small variants.
				bool primary = type is "logo" or "portrait" or "flag";
				string key = $"{kind}:{m.Groups["id"].Value}";
				if (!primary && _files.ContainsKey(key))
				{
					continue;
				}
				string? file = Extensions.Select(e => from + e).Where(present.Contains).Select(name => Path.Combine(dir, name)).FirstOrDefault();
				if (file != null && (primary || !_files.ContainsKey(key)))
				{
					_files[key] = file;
				}
			}
		}
		catch (Exception)
		{
			// A broken config.xml in one pack should not stop the others.
		}
	}

	public ImageSource? Club(uint id) => Get("club:" + id, 96);

	public ImageSource? Person(uint id) => Get("person:" + id, 160);

	private ImageSource? Get(string key, int size)
	{
		if (!_files.TryGetValue(key, out string? file))
		{
			return null;
		}
		return _images.GetOrAdd(key, _ =>
		{
			try
			{
				var image = new BitmapImage();
				image.BeginInit();
				image.UriSource = new Uri(file);
				image.CacheOption = BitmapCacheOption.OnLoad;
				image.DecodePixelHeight = size;
				image.EndInit();
				image.Freeze();
				return image;
			}
			catch (Exception)
			{
				return null;
			}
		});
	}
}
