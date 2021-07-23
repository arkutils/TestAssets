<Query Kind="Program">
  <Output>DataGrids</Output>
  <Namespace>System.Collections.Concurrent</Namespace>
  <Namespace>System.ComponentModel.DataAnnotations</Namespace>
  <Namespace>System.ComponentModel.DataAnnotations.Schema</Namespace>
  <Namespace>System.Diagnostics.CodeAnalysis</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
</Query>

async Task Main()
{
	var repoDirectory = Path.GetDirectoryName(Util.CurrentQueryPath);
	var baseDirectory = new DirectoryInfo(@$"{repoDirectory}\Cooked\Unreal\401");

	var excludes = new string[] { };

	var includes = new string[] { "." };

	var assetFiles = baseDirectory
		.EnumerateFiles("*.uasset", SearchOption.AllDirectories)
		.Where(x => !excludes.Any(p => Regex.IsMatch(p, ArchiveReader.FileSystemPathToAssetPath(x.FullName, baseDirectory.FullName), RegexOptions.IgnoreCase)))
		.Where(x => includes.Any(p => Regex.IsMatch(p, ArchiveReader.FileSystemPathToAssetPath(x.FullName, baseDirectory.FullName), RegexOptions.IgnoreCase)));

	foreach (var file in assetFiles)
	{
		try
		{
			var reader = new ArchiveReader(file, baseDirectory);
			var archive = reader.ReadArchive();
			var outputPath = Path.ChangeExtension(file.FullName, ".md");
			var markdown = DescribeChildren(reader.RootElement);

			await File.WriteAllTextAsync(outputPath, markdown);
		}
		catch (Exception ex)
		{
			ex.Message.Dump(file.FullName);
		}
	}
}

public List<ArchiveElement> GetNodes(ArchiveElement element)
{
	void GetNodes(ArchiveElement element, List<ArchiveElement> list)
	{
		list.Add(element);

		foreach (var child in element.Children)
		{
			GetNodes(child, list);
		}
	}

	var list = new List<ArchiveElement>();

	GetNodes(element, list);

	return list;
}

public bool IsRooted(ArchiveElement element)
{
	var roots = new[] { "Archive", "PackageFileSummary", "Import", "Import", "Export", null };
	
	return roots.Contains(element.Type)
		|| element.Parent?.Type == "Archive" && element.Type == "List"
		|| element.Parent?.Type == "Export" && element.Type == "Property[]";
}

public string DescribeChildren(ArchiveElement root)
{
	var lastOffset = 0;
	var builder = new StringBuilder();
	var inCodeBlock = false;

	var last = root;

	foreach (var element in GetNodes(root).OrderBy(x => x.StartPosition))
	{
		if (element.Type == "Archive")
		{
			continue;
		}

		var indentLevel = 0;

		var isRooted = IsRooted(element);

		var needsBreak = true;

		if (isRooted)
		{
			if (inCodeBlock)
			{
				builder.AppendLine("```");
				builder.AppendLine();
				inCodeBlock = false;
				needsBreak = false;
			}
		}
		else
		{
			if (!inCodeBlock)
			{
				builder.AppendLine("```");
				needsBreak = false;
				inCodeBlock = true;
			}
			
			indentLevel = element.Ancestors.TakeWhile(x => !IsRooted(x)).Count();
		}

		var indent = new string(' ', indentLevel * 2);

		if (element.StartPosition != lastOffset)
		{
			if (needsBreak)
			{
				builder.AppendLine();
			}

			if (!inCodeBlock)
			{
				builder.AppendLine($"# UNKNOWN @ {lastOffset}");
				builder.AppendLine("```");
				builder.AppendLine(BitConverter.ToString(root.Data[lastOffset..element.StartPosition]));
				builder.AppendLine("```");
			}
			else
			{
				builder.AppendLine($"UNKNOWN @ {lastOffset}");
				builder.AppendLine(BitConverter.ToString(root.Data[lastOffset..element.StartPosition]));
			}

			builder.AppendLine();
			lastOffset = element.StartPosition;
			needsBreak = false;
		}

		if (needsBreak)
		{
			builder.AppendLine();
		}

		if (isRooted)
		{
			var prefix = element.Type == "Import" || element.Type == "Export" ? "##" : "#";

			if (element.Parent?.Type == "Export" && element.Type == "Property[]")
			{
				var exportElement = element.Parent;
				var export = (ObjectExport)element.Parent.Value;
				builder.AppendLine(ReduceWhitespace($"# Export {exportElement.Name} Data, {export.SerialSize} bytes @ {export.SerialOffset}"));
			}
			else if (element.DescribeValue)
			{
				builder.AppendLine($"{prefix} {element.Name}: {element.Value} @ {element.StartPosition}");
			}
			else
			{
				builder.AppendLine($"{prefix} {element.Name} @ {element.StartPosition}");
			}
		}
		else
		{
			if (element.DescribeValue)
			{
				builder.AppendLine(indent + ReduceWhitespace($"{element.Name} ({element.Type}): {element.Value} @ {element.StartPosition}"));
			}
			else
			{
				builder.AppendLine(indent + ReduceWhitespace($"{element.Name} ({element.Type}) @ {element.StartPosition}"));
			}

			if (!element.Children.Any())
			{
				//Leaf nodes do actual reading, so they move the lastOffset
				if (element.Data.Any())
				{
					builder.AppendLine(indent + BitConverter.ToString(element.Data));
				}
				lastOffset = element.EndPosition;
			}
		}

		last = element;
	}

	if (inCodeBlock)
	{
		builder.AppendLine("```");
	}

	if (lastOffset != root.Data.Length)
	{
		builder.AppendLine();
		builder.AppendLine($"# UNKNOWN @ {lastOffset}");
		builder.AppendLine("```");
		builder.AppendLine(BitConverter.ToString(root.Data[lastOffset..]));
		builder.AppendLine("```");
	}

	return builder.ToString().Trim();
}

public string ReduceWhitespace(string input)
{
	return Regex.Replace(input, " +", " ").Trim();
}

public class Archive
{
	public string GamePath { get; set; }
	public List<string> Names { get; set; }
	public List<ObjectImport> Imports { get; set; }
	public List<ObjectExport> Exports { get; set; }

	public uint Tag { get; set; }
	public int LegacyFileVersion { get; set; }
	public int LegacyUE3Version { get; set; }
	public int FileVersionUE { get; set; }
	public int FileVersionLicenseeUE { get; set; }
	public int CustomVersionCount { get; set; }
	public int TotalHeaderSize { get; set; }
	public string FolderName { get; set; }
	public uint PackageFlags { get; set; }
	public int NameCount { get; set; }
	public int NameOffset { get; set; }
	public string LocalizationId { get; set; }
	public int GatherableTextDataCount { get; set; }
	public int GatherableTextDataOffset { get; set; }
	public int ExportCount { get; set; }
	public int ExportOffset { get; set; }
	public int ImportCount { get; set; }
	public int ImportOffset { get; set; }
	public int DependsOffset { get; set; }
	public int SoftPackageReferencesCount { get; set; }
	public int SoftPackageReferencesOffset { get; set; }
	public int SearchableNamesOffset { get; set; }
	public int ThumbnailTableOffset { get; set; }
	public Guid Guid { get; set; }
	public int GenerationCount { get; set; }
	public string SavedByEngineVersion { get; set; }
	public int EngineChangelist { get; set; }
	public string CompatibleWithEngineVersion { get; set; }
	public int CompressionFlags { get; set; }
	public int CompressedChunkCount { get; set; }
	public uint PackageSource { get; set; }
	public int AdditionalPackagesToCookCount { get; set; }
	public int NumTextureAllocations { get; set; }
	public int AssetRegistryDataOffset { get; set; }
	public long BulkDataStartOffset { get; set; }
	public int WorldTileInfoDataOffset { get; set; }
	public List<int> ChunkIDs { get; set; }
	public int PreloadDependencyCount { get; set; }
	public int PreloadDependencyOffset { get; set; }

	public List<GenerationInfo> Generations { get; set; }
	public List<CompressedChunk> CompressedChunks { get; set; }
	public List<GatherableTextData> GatherableTextData { get; set; }

	public Archive Deserialize(ArchiveReader reader)
	{
		using (reader.StartElement("PackageFileSummary", "PackageFileSummary"))
		{
			Tag = reader.ReadUInt32("Tag");
			LegacyFileVersion = reader.ReadInt32("LegacyFileVersion");

			if (LegacyFileVersion != -4)
			{
				LegacyUE3Version = reader.ReadInt32("LegacyUE3Version");
			}

			FileVersionUE = reader.ReadInt32("FileVersionUE");
			FileVersionLicenseeUE = reader.ReadInt32("FileVersionLicenseeUE");

			CustomVersionCount = reader.ReadInt32("CustomVersionCount");

			if (CustomVersionCount != 0)
			{
				throw new Exception("Custom versions not supported");
			}

			TotalHeaderSize = reader.ReadInt32("TotalHeaderSize");
			FolderName = reader.ReadString("FolderName");
			PackageFlags = reader.ReadUInt32("PackageFlags");
			NameCount = reader.ReadInt32("NameCount");
			NameOffset = reader.ReadInt32("NameOffset");

			if (FileVersionUE >= 516)
			{
				LocalizationId = reader.ReadString("LocalizationId");
			}

			if (FileVersionUE >= EngineVersions.VER_UE4_SERIALIZE_TEXT_INARCHIVES_459)
			{
				GatherableTextDataCount = reader.ReadInt32("GatherableTextDataCount");
				GatherableTextDataOffset = reader.ReadInt32("GatherableTextDataOffset");
			}

			ExportCount = reader.ReadInt32("ExportCount");
			ExportOffset = reader.ReadInt32("ExportOffset");
			ImportCount = reader.ReadInt32("ImportCount");
			ImportOffset = reader.ReadInt32("ImportOffset");
			DependsOffset = reader.ReadInt32("DependsOffset");

			if (FileVersionUE >= 384)
			{
				SoftPackageReferencesCount = reader.ReadInt32("SoftPackageReferencesCount");
				SoftPackageReferencesOffset = reader.ReadInt32("SoftPackageReferencesOffset");
			}

			if (FileVersionUE >= 510)
			{
				SearchableNamesOffset = reader.ReadInt32("SearchableNamesOffset");
			}

			ThumbnailTableOffset = reader.ReadInt32("ThumbnailTableOffset");
			Guid = reader.ReadGuid("Guid");
			GenerationCount = reader.ReadInt32("GenerationCount");
			Generations = reader.ReadList("Generations", GenerationCount, i => reader.Read("Generation", i.ToString(), false, () => new GenerationInfo().Deserialize(reader, this)));

			if (FileVersionUE >= 336)
			{
				SavedByEngineVersion = new EngineVersion().Deserialize(reader, this).ToString();
			}
			else
			{
				EngineChangelist = reader.ReadInt32("EngineChangelist");
			}

			if (FileVersionUE >= 444)
			{
				CompatibleWithEngineVersion = new EngineVersion().Deserialize(reader, this).ToString();
			}

			CompressionFlags = reader.ReadInt32("CompressionFlags");
			CompressedChunkCount = reader.ReadInt32("CompressedChunkCount");
			CompressedChunks = reader.ReadList("CompressedChunks", CompressedChunkCount, i => reader.Read("Chunk", i.ToString(), false, () => new CompressedChunk().Deserialize(reader, this)));
			PackageSource = reader.ReadUInt32("PackageSource");

			if (FileVersionLicenseeUE >= 10)
			{
				// This field isn't present in some older ARK mods
				reader.Seek(8, SeekOrigin.Current);
			}

			AdditionalPackagesToCookCount = reader.ReadInt32("AdditionalPackagesToCookCount");
			for (int i = 0; i < AdditionalPackagesToCookCount; i++)
			{
				reader.ReadString("AdditionalPackagesToCook");
			}

			if (LegacyFileVersion >= -7)
			{
				NumTextureAllocations = reader.ReadInt32("NumTextureAllocations");
			}

			AssetRegistryDataOffset = reader.ReadInt32("AssetRegistryDataOffset");
			BulkDataStartOffset = reader.ReadInt32("BulkDataStartOffset");

			if (FileVersionUE >= EngineVersions.VER_UE4_WORLD_LEVEL_INFO_224)
			{
				WorldTileInfoDataOffset = reader.ReadInt32("WorldTileInfoDataOffset");
			}

			if (FileVersionUE >= EngineVersions.VER_UE4_CHANGED_CHUNKID_TO_BE_AN_ARRAY_OF_CHUNKIDS_326)
			{
				var count = reader.ReadInt32("ChunkIDCount");
				ChunkIDs = reader.ReadList("ChunkIDs", count, i => reader.ReadInt32("ChunkID"));
			}
			else if (FileVersionUE >= 278)
			{
				ChunkIDs = new List<int> { reader.ReadInt32("ChunkID") };
			}

			if (FileVersionUE >= 507)
			{
				PreloadDependencyCount = reader.ReadInt32("PreloadDependencyCount");
				PreloadDependencyOffset = reader.ReadInt32("PreloadDependencyOffset");
			}
		}

		reader.Seek(NameOffset);
		Names = reader.ReadList("Names", NameCount, i => reader.ReadString(i.ToString()));

		if (FileVersionUE >= EngineVersions.VER_UE4_SERIALIZE_TEXT_INARCHIVES_459)
		{
			reader.Seek(GatherableTextDataOffset);
			GatherableTextData = reader.ReadList("GatherableTextData", GatherableTextDataCount, i => new GatherableTextData().Deserialize(reader, this));
		}

		reader.ExecuteAtOffset(ImportOffset, () =>
		{
			Imports = Enumerable.Range(0, ImportCount).Select(x => new ObjectImport()).ToList();

			using var element = reader.StartElement("List", "Imports");
			element.Value = Imports;
			element.DescribeValue = false;

			for (var i = 0; i < ImportCount; i++)
			{
				reader.Read("Import", (-i - 1).ToString(), () => Imports[i].Deserialize(reader, this));
			}
		});

		reader.ExecuteAtOffset(ExportOffset, () =>
		{
			Exports = Enumerable.Range(0, ExportCount).Select(x => new ObjectExport()).ToList();

			using var element = reader.StartElement("List", "Exports");
			element.Value = Exports;
			element.DescribeValue = false;

			for (var i = 0; i < ExportCount; i++)
			{
				reader.Read("Export", (i + 1).ToString(), () => Exports[i].Deserialize(reader, this));
			}
		});

		return this;
	}

	internal IObjectResource GetImportOrExport(int index)
	{
		if (index > 0)
		{
			return GetExport(index);
		}

		if (index < 0)
		{
			return GetImport(index);
		}

		return null;
	}

	internal ObjectImport GetImport(int index)
	{
		if (index == 0)
		{
			return null;
		}

		if (index < 0)
		{
			var usedIndex = -index - 1;
			if (Imports.Count > usedIndex)
			{
				return Imports[usedIndex];
			}
		}

		throw new Exception("Bad import index");
	}

	internal ObjectExport GetExport(int index)
	{
		if (index == 0)
		{
			return null;
		}

		if (index > 0)
		{
			var usedIndex = index - 1;
			if (Exports.Count > usedIndex)
			{
				return Exports[usedIndex];
			}
		}

		throw new Exception("Bad export index");
	}
}

public class ObjectImport : IObjectResource
{
	public string FullName => Outer == null ? ObjectName : $"{Outer.FullName}.{ObjectName}";

	public ObjectImport Outer { get; set; }

	public string ObjectName { get; set; }

	public string ClassPackage { get; set; }

	public string ClassName { get; set; }

	public int OuterIndex { get; set; }

	IObjectResource IObjectResource.Outer => Outer;

	public override string ToString() => FullName;

	public ObjectImport Deserialize(ArchiveReader reader, Archive archive)
	{
		ClassPackage = reader.ReadName("ClassPackage", archive);
		ClassName = reader.ReadName("ClassName", archive);

		Outer = reader.Read("ObjectReference", "Outer", () =>
		{
			OuterIndex = reader.ReadInt32();
			return archive.GetImport(OuterIndex);
		});

		ObjectName = reader.ReadName("ObjectName", archive);

		return this;
	}
}

public class ObjectExport : IObjectResource
{
	public string FullName => (Outer == null ? ObjectName : $"{Outer.FullName}.{ObjectName}");
	public ObjectExport Outer { get; set; }
	public string ObjectName { get; set; }

	public IObjectResource Class { get; set; }
	public ObjectImport Super { get; set; }
	public IObjectResource Template { get; set; }

	public int ClassIndex { get; set; }

	public int SuperIndex { get; set; }

	public int TemplateIndex { get; set; }

	public int OuterIndex { get; set; }

	public uint ObjectFlags { get; set; }

	public long SerialSize { get; set; }

	public long SerialOffset { get; set; }

	public bool ForcedExport { get; set; }

	public bool NotForClient { get; set; }

	public bool NotForServer { get; set; }

	public Guid PackageGuid { get; set; }

	public uint PackageFlags { get; set; }

	public bool NotAlwaysLoadedForEditorGame { get; set; }

	public bool IsAsset { get; set; }

	public int FirstExportDependency { get; set; }

	public int SerializationBeforeSerializationDependencies { get; set; }

	public int CreateBeforeSerializationDependencies { get; set; }

	public int SerializationBeforeCreateDependencies { get; set; }

	public int CreateBeforeCreateDependencies { get; set; }

	public List<Property> Properties { get; set; }

	IObjectResource IObjectResource.Outer => Outer;

	public override string ToString() => FullName;
	
	public byte[] ExtraExportData { get; set; }

	public ObjectExport Deserialize(ArchiveReader reader, Archive archive)
	{
		Class = reader.Read("ObjectReference", "Class", () =>
		{
			ClassIndex = reader.ReadInt32();
			return archive.GetImportOrExport(ClassIndex);
		});

		Super = reader.Read("ObjectReference", "Super", () =>
		{
			SuperIndex = reader.ReadInt32();
			return archive.GetImport(SuperIndex);
		});

		if (archive.FileVersionUE >= 508)
		{
			Template = reader.Read("ObjectReference", "Template", () =>
			{
				TemplateIndex = reader.ReadInt32();
				return archive.GetImportOrExport(TemplateIndex);
			});
		}

		Outer = reader.Read("ObjectReference", "Outer", () =>
		{
			OuterIndex = reader.ReadInt32();
			return archive.GetExport(OuterIndex);
		});

		ObjectName = reader.ReadName("ObjectName", archive);
		ObjectFlags = reader.ReadUInt32("ObjectFlags");

		if (archive.FileVersionUE >= 511)
		{
			SerialSize = reader.ReadInt64("SerialSize");
			SerialOffset = reader.ReadInt64("SerialOffset");
		}
		else
		{
			SerialSize = reader.ReadInt32("SerialSize");
			SerialOffset = reader.ReadInt32("SerialOffset");
		}

		ForcedExport = reader.ReadBoolean32("ForcedExport");
		NotForClient = reader.ReadBoolean32("NotForClient");
		NotForServer = reader.ReadBoolean32("NotForServer");
		PackageGuid = reader.ReadGuid("PackageGuid");
		PackageFlags = reader.ReadUInt32("PackageFlags");

		if (archive.FileVersionUE >= 365)
		{
			NotAlwaysLoadedForEditorGame = reader.ReadBoolean32("NotAlwaysLoadedForEditorGame");
		}

		if (archive.FileVersionUE >= 485)
		{
			IsAsset = reader.ReadBoolean32("IsAsset");
		}

		if (archive.FileVersionUE >= 507)
		{
			FirstExportDependency = reader.ReadInt32("FirstExportDependency");
			SerializationBeforeSerializationDependencies = reader.ReadInt32("SerializationBeforeSerializationDependencies");
			CreateBeforeSerializationDependencies = reader.ReadInt32("CreateBeforeSerializationDependencies");
			SerializationBeforeCreateDependencies = reader.ReadInt32("SerializationBeforeCreateDependencies");
			CreateBeforeCreateDependencies = reader.ReadInt32("CreateBeforeCreateDependencies");
		}

		var offset = reader.Position;
		reader.Seek(SerialOffset);
			
		Properties = reader.Read("Property[]", "Properties", () => ReadProperties(reader, archive, this));
		
		var end = SerialOffset + SerialSize;
		var unknownBytes = end - reader.Position;
		
		if(unknownBytes > 0)
		{
			ExtraExportData = reader.ReadBytes((int)unknownBytes, "ExtraExportData");
		}
		
		reader.Seek(offset);

		return this;
	}

	private List<Property> ReadProperties(ArchiveReader reader, Archive archive, ObjectExport export)
	{
		var entries = new List<Property>();
		var index = 0;
		var endOffset = export.SerialSize + export.SerialOffset - 1;
		while (reader.Position <= endOffset)
		{
			using (var propertyElement = reader.StartElement("Property"))
			{
				var property = new Property().Deserialize(reader, archive, export);
				propertyElement.Name = property.Tag?.Name;
				entries.Add(property);
				if (property.Tag.Name == "None")
				{
					break;
				}
			}
			index++;
		}

		return entries;
	}
}

public class GatherableTextData
{
	public string NamespaceName { get; set; }
	//public TextSourceData SourceData { get; set; }
	//public List<TextSourceSiteContext> SourceSiteContexts { get; set; }

	public GatherableTextData Deserialize(ArchiveReader reader, Archive archive)
	{
		NamespaceName = reader.ReadString("NamespaceName");

		return this;
	}
};

public class GenerationInfo
{
	public int NameCount { get; set; }
	public int ExportCount { get; set; }

	public GenerationInfo Deserialize(ArchiveReader reader, Archive archive)
	{
		NameCount = reader.ReadInt32("NameCount");
		ExportCount = reader.ReadInt32("ExportCount");

		return this;
	}
}

public class CompressedChunk
{
	public int UncompressedOffset { get; set; }
	public int UncompressedSize { get; set; }
	public int CompressedOffset { get; set; }
	public int CompressedSize { get; set; }

	public CompressedChunk Deserialize(ArchiveReader reader, Archive archive)
	{
		UncompressedOffset = reader.ReadInt32("UncompressedOffset");
		UncompressedSize = reader.ReadInt32("UncompressedSize");
		CompressedOffset = reader.ReadInt32("CompressedOffset");
		CompressedSize = reader.ReadInt32("CompressedSize");

		return this;
	}
}

public class NameIndex
{
	public uint Index { get; set; }
	public uint Instance { get; set; }
	public string Value { get; set; }

	public override string ToString() => Value;

	public NameIndex Deserialize(ArchiveReader reader, Archive archive)
	{
		Index = reader.ReadUInt32();
		Instance = reader.ReadUInt32();

		string nameString = archive.Names.Count > Index ? archive.Names[(int)Index] : null;

		Value = Instance > 0 ? $"{nameString}_{Instance - 1}" : nameString;

		return this;
	}
}

public class PackageIndex
{
	public int Index { get; set; }
	public object Referenced { get; set; }

	public override string ToString() =>
		Index == 0 ? "None" :
		Referenced != null ? Referenced.ToString()
		: Index.ToString();

	public PackageIndex Deserialize(ArchiveReader reader, Archive archive)
	{
		Index = reader.ReadInt32("Index");

		if (Index < 0)
		{
			var usedIndex = -Index - 1;
			if (usedIndex < archive.Imports.Count)
			{
				Referenced = archive.Imports[usedIndex];
			}
		}

		if (Index > 0)
		{
			var usedIndex = Index - 1;
			if (usedIndex < archive.Exports.Count)
			{
				Referenced = archive.Exports[usedIndex];
			}
		}

		return this;
	}
}

public class PropertyTag
{
	public string Type { get; set; }     // Type of property

	public bool BoolVal { get; set; }// a boolean property's value (never need to serialize data for bool properties except here)
	public string Name { get; set; }     // Name of property.
	public string StructName { get; set; }   // Struct name if FStructProperty.
	public string EnumName { get; set; } // Enum name if FByteProperty or FEnumProperty
	public string InnerType { get; set; }    // Inner type if FArrayProperty, FSetProperty, or FMapProperty
	public string ValueType { get; set; }    // Value type if UMapPropery
	public int Size { get; set; }   // Property size.
	public int ArrayIndex { get; set; } // Index if an array; else 0.
	public long SizeOffset { get; set; } // location in stream of tag size member
	public Guid StructGuid { get; set; }
	public bool HasPropertyGuid { get; set; }
	public Guid PropertyGuid { get; set; }

	public override string ToString() => string.IsNullOrWhiteSpace(Type) ? Name : $"{Type} {Name}";

	public PropertyTag Deserialize(ArchiveReader reader, Archive archive)
	{
		Name = reader.ReadName("Name", archive);

		if (Name == "None")
		{
			return this;
		}

		var type = reader.Read("NameIndex", "Type", () => new NameIndex().Deserialize(reader, archive));

		Type = type.Value;
		Size = reader.ReadInt32("Size");
		ArrayIndex = reader.ReadInt32("ArrayIndex");

		if (type.Instance == 0)
		{
			// only need to serialize this for structs
			if (Type == "StructProperty")
			{
				StructName = reader.ReadName("StructName", archive);
				if (archive.FileVersionUE >= EngineVersions.VER_UE4_STRUCT_GUID_IN_PROPERTY_TAG_441)
				{
					StructGuid = reader.ReadGuid("StructGuid");
				}
			}
			// only need to serialize this for bools
			else if (Type == "BoolProperty")
			{
				BoolVal = reader.ReadBoolean("BoolVal");
			}
			// only need to serialize this for bytes/enums
			else if (Type == "ByteProperty")
			{
				EnumName = reader.ReadName("EnumName", archive);
			}
			else if (Type == "EnumProperty")
			{
				EnumName = reader.ReadName("EnumName", archive);
			}
			// only need to serialize this for arrays
			else if (Type == "ArrayProperty")
			{
				if (archive.FileVersionUE >= EngineVersions.VAR_UE4_ARRAY_PROPERTY_INNER_TAGS_282)
				{
					InnerType = reader.ReadName("InnerType", archive);
				}
			}
			else if (archive.FileVersionUE >= EngineVersions.VER_UE4_PROPERTY_TAG_SET_MAP_SUPPORT_509)
			{
				if (Type == "SetProperty")
				{
					InnerType = reader.ReadName("InnerType", archive);
				}
				else if (Type == "MapProperty")
				{
					InnerType = reader.ReadName("InnerType", archive);
					ValueType = reader.ReadName("ValueType", archive);
				}
			}
		}

		// Property tags to handle renamed blueprint properties effectively.
		if (archive.FileVersionUE >= EngineVersions.VER_UE4_PROPERTY_GUID_IN_PROPERTY_TAG_503)
		{
			HasPropertyGuid = reader.ReadBoolean("HasPropertyGuid");
			if (HasPropertyGuid)
			{
				PropertyGuid = reader.ReadGuid("PropertyGuid");
			}
		}

		return this;
	}
}

public class Property
{
	public Archive Archive { get; set; }
	public ObjectExport Export { get; set; }
	public PropertyTag Tag { get; set; }
	public Byte[] Data { get; set; }
	public Exception Exception { get; set; }

	public override string ToString() => Tag.Name;

	public Property Deserialize(ArchiveReader reader, Archive archive, ObjectExport export)
	{
		Archive = archive;
		Export = export;

		try
		{
			Tag = reader.Read("Tag", null, () => new PropertyTag().Deserialize(reader, archive));

			if (Tag.Name == "None")
			{
				return this;
			}

			// Skip the rest of the property
			if (Tag.SizeOffset > 0)
			{
				reader.Seek(Tag.SizeOffset);
			}

			Data = reader.ReadBytes(Tag.Size, "Data");
		}
		catch (Exception ex)
		{
			Exception = ex;
		}

		return this;
	}
}

public class EngineVersion
{
	public ushort Major { get; set; }
	public ushort Minor { get; set; }
	public ushort Patch { get; set; }
	public uint Changelist { get; set; }
	public string Branch { get; set; }

	public override string ToString()
	{
		return string.IsNullOrEmpty(Branch)
			? $"{Major}.{Minor}.{Patch}.{Changelist}"
			: $"{Major}.{Minor}.{Patch}.{Changelist}-{Branch}";
	}

	public EngineVersion Deserialize(ArchiveReader reader, Archive archive)
	{
		Major = reader.ReadUInt16("Major");
		Minor = reader.ReadUInt16("Minor");
		Patch = reader.ReadUInt16("Patch");
		Changelist = reader.ReadUInt32("Changelist");
		Branch = reader.ReadString("Branch");

		return this;
	}
}

public interface IObjectResource
{
	string FullName { get; }
	IObjectResource Outer { get; }
}

public class ArchiveReader : IDisposable
{
	private readonly BinaryReader _reader;
	private readonly string _gamePath;
	private readonly Stack<ArchiveElement> _elementStack;
	private bool disposedValue;

	public ArchiveReader(FileInfo file, DirectoryInfo baseDirectory)
	{
		_reader = new BinaryReader(file.OpenRead());
		_gamePath = ArchiveReader.FileSystemPathToAssetPath(file.FullName, baseDirectory.FullName);
		_elementStack = new();
	}

	public ArchiveElement RootElement { get; private set; }

	public long Position => _reader.BaseStream.Position;

	public static string FileSystemPathToAssetPath(string path, string basePath)
	{
		if (!path.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
		{
			throw new Exception("Path does not start with basePath");
		}

		var normalizedBasePath = Regex.Replace(basePath.Replace('\\', '/'), "/{2}", "/").TrimEnd('/');
		var normalizedPath = Regex.Replace(path.Substring(0, path.Length - 7).Replace('\\', '/'), "/{2}", "/").TrimEnd('/');

		if (normalizedPath.Equals(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
		{
			return "/Game";
		}

		if (!normalizedPath.StartsWith(normalizedBasePath + "/", StringComparison.OrdinalIgnoreCase))
		{
			throw new Exception("Path does not start with basePath");
		}

		return "/Game" + normalizedPath.Substring(normalizedBasePath.Length);
	}

	public static string AssetPathToFileSystemPath(string path, string basePath)
	{
		var normalizedBasePath = Regex.Replace(basePath.Replace('\\', '/'), "/{2}", "/").TrimEnd('/');
		var normalizedPath = Regex.Replace(path, "/{2}", "/").TrimStart('/');
		var assetRelativePath = Regex.Replace(normalizedPath, "^Game(/|$)", string.Empty, RegexOptions.IgnoreCase);

		var result = $"{normalizedBasePath}/{assetRelativePath}";

		if (System.IO.Path.DirectorySeparatorChar == '\\')
		{
			result = result.Replace('/', '\\');
		}

		return $"{result}.uasset";
	}

	public void Seek(long offset, SeekOrigin origin = SeekOrigin.Begin) => _reader.BaseStream.Seek(offset, origin);

	public void ExecuteAtOffset(long offset, Action action)
	{
		var originalOffset = Position;

		Seek(offset);

		action();

		Seek(originalOffset);
	}

	public T ReadAtOffset<T>(string type, string name, long offset, Func<T> reader)
	{
		var originalOffset = Position;

		Seek(offset);

		var result = Read(type, name, reader);

		Seek(originalOffset);

		return result;
	}

	public T Read<T>(string type, string name, Func<T> reader) => Read(type, name, true, reader);

	public T Read<T>(string type, string name, bool describeValue, Func<T> reader)
	{
		using (var element = StartElement(type, name))
		{
			var result = reader.Invoke();
			element.Value = result;
			element.DescribeValue = describeValue;
			return result;
		}
	}

	public ushort ReadUInt16(string name) => Read("UInt16", name, () => _reader.ReadUInt16());

	public short ReadInt16(string name) => Read("Int16", name, () => _reader.ReadInt16());

	public int ReadInt32() => _reader.ReadInt32();

	public uint ReadUInt32() => _reader.ReadUInt32();

	public uint ReadUInt32(string name) => Read("UInt32", name, () => _reader.ReadUInt32());

	public int ReadInt32(string name) => Read("Int32", name, () => _reader.ReadInt32());

	public ulong ReadUInt64(string name) => Read("UInt64", name, () => _reader.ReadUInt64());

	public long ReadInt64(string name) => Read("Int64", name, () => _reader.ReadInt64());

	public bool ReadBoolean(string name) => Read("Boolean", name, () => _reader.ReadBoolean());

	public byte[] ReadBytes(int count, string name = null) => Read("Byte[]", name, false, () => _reader.ReadBytes(count));

	public bool ReadBoolean32(string name) => Read("Boolean32", name, () => _reader.ReadUInt32() != 0);

	public Guid ReadGuid(string name) => Read("Guid", name, () => new Guid(_reader.ReadBytes(16)));

	public string ReadName(string name, Archive archive) => Read("NameIndex", name, () => new NameIndex().Deserialize(this, archive).Value);

	public List<T> ReadList<T>(string name, int count, Func<int, T> selector) => Read("List", name, false, () => Enumerable.Range(0, count).Select(selector).ToList());

	public string ReadString(string name)
	{
		return Read("String", name, () =>
		{
			var length = _reader.ReadInt32();

			if (length < 0)
			{
				return ReadNullTerminatedString(Encoding.Unicode, 2, -length);
			}

			if (length > 0)
			{
				return ReadNullTerminatedString(Encoding.UTF8, 1, length);
			}

			return string.Empty;
		});
	}

	public Archive ReadArchive()
	{
		_elementStack.Clear();
		
		var archive = new Archive { GamePath = _gamePath };

		using (RootElement = StartElement(nameof(Archive), _gamePath))
		{
			RootElement.Value = archive;
			archive.Deserialize(this);
		}
		
		RootElement.EndPosition = (int)_reader.BaseStream.Length;
		return archive;
	}

	public ArchiveElement StartElement(string type, string name = default)
	{
		var element = new ArchiveElement(this) { Type = type, Name = name, StartPosition = (int)Position };

		if (_elementStack.TryPeek(out var currentElement))
		{
			currentElement.Children.Add(element);
			element.Parent = currentElement;
		}

		_elementStack.Push(element);
		return element;
	}

	public void EndElement(ArchiveElement element = default)
	{
		if (!_elementStack.TryPeek(out var currentElement) || (element != null && currentElement != element))
		{
			throw new Exception("Stack pop exception");
		}

		currentElement.EndPosition = (int)Position;
		currentElement.Children.Sort((a, b) => a.StartPosition - b.StartPosition);

		if (currentElement != RootElement)
		{
			_elementStack.Pop();
		}
	}

	public void Dispose()
	{
		// Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (!disposedValue)
		{
			if (disposing)
			{
				_reader.Dispose();
			}

			// TODO: free unmanaged resources (unmanaged objects) and override finalizer
			// TODO: set large fields to null
			disposedValue = true;
		}
	}

	private string ReadNullTerminatedString(Encoding encoding, int bytesPerChar, int length)
	{
		var bytes = _reader.ReadBytes(length * bytesPerChar);

		return encoding.GetString(bytes)[..^1];
	}
}

public static class EngineVersions
{
	public const int VER_UE4_OLDEST_LOADABLEARCHIVE_214 = 214;
	public const int VER_UE4_BLUEPRINT_VARS_NOT_READ_ONLY_215 = 215;
	public const int VER_UE4_STATIC_MESH_STORE_NAV_COLLISION_216 = 216;
	public const int VER_UE4_ATMOSPHERIC_FOG_DECAY_NAME_CHANGE_217 = 217;
	public const int VER_UE4_SCENECOMP_TRANSLATION_TO_LOCATION_218 = 218;
	public const int VER_UE4_MATERIAL_ATTRIBUTES_REORDERING_219 = 219;
	public const int VER_UE4_COLLISION_PROFILE_SETTING_220 = 220;
	public const int VER_UE4_BLUEPRINT_SKEL_TEMPORARY_TRANSIENT_221 = 221;
	public const int VER_UE4_BLUEPRINT_SKEL_SERIALIZED_AGAIN_222 = 222;
	public const int VER_UE4_BLUEPRINT_SETS_REPLICATION_223 = 223;
	public const int VER_UE4_WORLD_LEVEL_INFO_224 = 224;
	public const int VER_UE4_AFTER_CAPSULE_HALF_HEIGHT_CHANGE_225 = 225;
	public const int VER_UE4_ADDED_NAMESPACE_AND_KEY_DATA_TO_FTEXT_226 = 226;
	public const int VER_UE4_ATTENUATION_SHAPES_227 = 227;
	public const int VER_UE4_LIGHTCOMPONENT_USE_IES_TEXTURE_MULTIPLIER_ON_NON_IES_BRIGHTNESS_228 = 228;
	public const int VER_UE4_REMOVE_INPUT_COMPONENTS_FROM_BLUEPRINTS_229 = 229;
	public const int VER_UE4_VARK2NODE_USE_MEMBERREFSTRUCT_230 = 230;
	public const int VER_UE4_REFACTOR_MATERIAL_EXPRESSION_SCENECOLOR_AND_SCENEDEPTH_INPUTS_231 = 231;
	public const int VER_UE4_SPLINE_MESH_ORIENTATION_232 = 232;
	public const int VER_UE4_REVERB_EFFECT_ASSET_TYPE_233 = 233;
	public const int VER_UE4_MAX_TEXCOORD_INCREASED_234 = 234;
	public const int VER_UE4_SPEEDTREE_STATICMESH_235 = 235;
	public const int VER_UE4_LANDSCAPE_COMPONENT_LAZY_REFERENCES_236 = 236;
	public const int VER_UE4_SWITCH_CALL_NODE_TO_USE_MEMBER_REFERENCE_237 = 237;
	public const int VER_UE4_ADDED_SKELETON_ARCHIVER_REMOVAL_238 = 238;
	public const int VER_UE4_ADDED_SKELETON_ARCHIVER_REMOVAL_SECOND_TIME_239 = 239;
	public const int VER_UE4_BLUEPRINT_SKEL_CLASS_TRANSIENT_AGAIN_240 = 240;
	public const int VER_UE4_ADD_COOKED_TO_UCLASS_241 = 241;
	public const int VER_UE4_DEPRECATED_STATIC_MESH_THUMBNAIL_PROPERTIES_REMOVED_242 = 242;
	public const int VER_UE4_COLLECTIONS_IN_SHADERMAPID_243 = 243;
	public const int VER_UE4_REFACTOR_MOVEMENT_COMPONENT_HIERARCHY_244 = 244;
	public const int VER_UE4_FIX_TERRAIN_LAYER_SWITCH_ORDER_245 = 245;
	public const int VER_UE4_ALL_PROPS_TO_CONSTRAINTINSTANCE_246 = 246;
	public const int VER_UE4_LOW_QUALITY_DIRECTIONAL_LIGHTMAPS_247 = 247;
	public const int VER_UE4_ADDED_NOISE_EMITTER_COMPONENT_248 = 248;
	public const int VER_UE4_ADD_TEXT_COMPONENT_VERTICAL_ALIGNMENT_249 = 249;
	public const int VER_UE4_ADDED_FBX_ASSET_IMPORT_DATA_250 = 250;
	public const int VER_UE4_REMOVE_LEVELBODYSETUP_251 = 251;
	public const int VER_UE4_REFACTOR_CHARACTER_CROUCH_252 = 252;
	public const int VER_UE4_SMALLER_DEBUG_MATERIALSHADER_UNIFORM_EXPRESSIONS_253 = 253;
	public const int VER_UE4_APEX_CLOTH_254 = 254;
	public const int VER_UE4_SAVE_COLLISIONRESPONSE_PER_CHANNEL_255 = 255;
	public const int VER_UE4_ADDED_LANDSCAPE_SPLINE_EDITOR_MESH_256 = 256;
	public const int VER_UE4_CHANGED_MATERIAL_REFACTION_TYPE_257 = 257;
	public const int VER_UE4_REFACTOR_PROJECTILE_MOVEMENT_258 = 258;
	public const int VER_UE4_REMOVE_PHYSICALMATERIALPROPERTY_259 = 259;
	public const int VER_UE4_PURGED_FMATERIAL_COMPILE_OUTPUTS_260 = 260;
	public const int VER_UE4_ADD_COOKED_TO_LANDSCAPE_261 = 261;
	public const int VER_UE4_CONSUME_INPUT_PER_BIND_262 = 262;
	public const int VER_UE4_SOUND_CLASS_GRAPH_EDITOR_263 = 263;
	public const int VER_UE4_FIXUP_TERRAIN_LAYER_NODES_264 = 264;
	public const int VER_UE4_RETROFIT_CLAMP_EXPRESSIONS_SWAP_265 = 265;
	public const int VER_UE4_REMOVE_LIGHT_MOBILITY_CLASSES_266 = 266;
	public const int VER_UE4_REFACTOR_PHYSICS_BLENDING_267 = 267;
	public const int VER_UE4_WORLD_LEVEL_INFO_UPDATED_268 = 268;
	public const int VER_UE4_STATIC_SKELETAL_MESH_SERIALIZATION_FIX_269 = 269;
	public const int VER_UE4_REMOVE_STATICMESH_MOBILITY_CLASSES_270 = 270;
	public const int VER_UE4_REFACTOR_PHYSICS_TRANSFORMS_271 = 271;
	public const int VER_UE4_REMOVE_ZERO_TRIANGLE_SECTIONS_272 = 272;
	public const int VER_UE4_CHARACTER_MOVEMENT_DECELERATION_273 = 273;
	public const int VER_UE4_CAMERA_ACTOR_USING_CAMERA_COMPONENT_274 = 274;
	public const int VER_UE4_CHARACTER_MOVEMENT_DEPRECATE_PITCH_ROLL_275 = 275;
	public const int VER_UE4_REBUILD_TEXTURE_STREAMING_DATA_ON_LOAD_276 = 276;
	public const int VER_UE4_SUPPORT_32BIT_STATIC_MESH_INDICES_277 = 277;
	public const int VER_UE4_ADDED_CHUNKID_TO_ASSETDATA_AND_UPACKAGE_278 = 278;
	public const int VER_UE4_CHARACTER_DEFAULT_MOVEMENT_BINDINGS_279 = 279;
	public const int VER_UE4_APEX_CLOTH_LOD_280 = 280;
	public const int VER_UE4_ATMOSPHERIC_FOG_CACHE_DATA_281 = 281;
	public const int VAR_UE4_ARRAY_PROPERTY_INNER_TAGS_282 = 282;
	public const int VER_UE4_KEEP_SKEL_MESH_INDEX_DATA_283 = 283;
	public const int VER_UE4_BODYSETUP_COLLISION_CONVERSION_284 = 284;
	public const int VER_UE4_REFLECTION_CAPTURE_COOKING_285 = 285;
	public const int VER_UE4_REMOVE_DYNAMIC_VOLUME_CLASSES_286 = 286;
	public const int VER_UE4_STORE_HASCOOKEDDATA_FOR_BODYSETUP_287 = 287;
	public const int VER_UE4_REFRACTION_BIAS_TO_REFRACTION_DEPTH_BIAS_288 = 288;
	public const int VER_UE4_REMOVE_SKELETALPHYSICSACTOR_289 = 289;
	public const int VER_UE4_PC_ROTATION_INPUT_REFACTOR_290 = 290;
	public const int VER_UE4_LANDSCAPE_PLATFORMDATA_COOKING_291 = 291;
	public const int VER_UE4_CREATEEXPORTS_CLASS_LINKING_FOR_BLUEPRINTS_292 = 292;
	public const int VER_UE4_REMOVE_NATIVE_COMPONENTS_FROM_BLUEPRINT_SCS_293 = 293;
	public const int VER_UE4_REMOVE_SINGLENODEINSTANCE_294 = 294;
	public const int VER_UE4_CHARACTER_BRAKING_REFACTOR_295 = 295;
	public const int VER_UE4_VOLUME_SAMPLE_LOW_QUALITY_SUPPORT_296 = 296;
	public const int VER_UE4_SPLIT_TOUCH_AND_CLICK_ENABLES_297 = 297;
	public const int VER_UE4_HEALTH_DEATH_REFACTOR_298 = 298;
	public const int VER_UE4_SOUND_NODE_ENVELOPER_CURVE_CHANGE_299 = 299;
	public const int VER_UE4_POINT_LIGHT_SOURCE_RADIUS_300 = 300;
	public const int VER_UE4_SCENE_CAPTURE_CAMERA_CHANGE_301 = 301;
	public const int VER_UE4_MOVE_SKELETALMESH_SHADOWCASTING_302 = 302;
	public const int VER_UE4_CHANGE_SETARRAY_BYTECODE_303 = 303;
	public const int VER_UE4_MATERIAL_INSTANCE_BASE_PROPERTY_OVERRIDES_304 = 304;
	public const int VER_UE4_COMBINED_LIGHTMAP_TEXTURES_305 = 305;
	public const int VER_UE4_BUMPED_MATERIAL_EXPORT_GUIDS_306 = 306;
	public const int VER_UE4_BLUEPRINT_INPUT_BINDING_OVERRIDES_307 = 307;
	public const int VER_UE4_FIXUP_BODYSETUP_INVALID_CONVEX_TRANSFORM_308 = 308;
	public const int VER_UE4_FIXUP_STIFFNESS_AND_DAMPING_SCALE_309 = 309;
	public const int VER_UE4_REFERENCE_SKELETON_REFACTOR_310 = 310;
	public const int VER_UE4_K2NODE_REFERENCEGUIDS_311 = 311;
	public const int VER_UE4_FIXUP_ROOTBONE_PARENT_312 = 312;
	public const int VER_UE4_TEXT_RENDER_COMPONENTS_WORLD_SPACE_SIZING_313 = 313;
	public const int VER_UE4_MATERIAL_INSTANCE_BASE_PROPERTY_OVERRIDES_PHASE_2_314 = 314;
	public const int VER_UE4_CLASS_NOTPLACEABLE_ADDED_315 = 315;
	public const int VER_UE4_WORLD_LEVEL_INFO_LOD_LIST_316 = 316;
	public const int VER_UE4_CHARACTER_MOVEMENT_VARIABLE_RENAMING_1_317 = 317;
	public const int VER_UE4_FSLATESOUND_CONVERSION_318 = 318;
	public const int VER_UE4_WORLD_LEVEL_INFO_ZORDER_319 = 319;
	public const int VER_UE4archive_REQUIRES_LOCALIZATION_GATHER_FLAGGING_320 = 320;
	public const int VER_UE4_BP_ACTOR_VARIABLE_DEFAULT_PREVENTING_321 = 321;
	public const int VER_UE4_TEST_ANIMCOMP_CHANGE_322 = 322;
	public const int VER_UE4_EDITORONLY_BLUEPRINTS_323 = 323;
	public const int VER_UE4_EDGRAPHPINTYPE_SERIALIZATION_324 = 324;
	public const int VER_UE4_NO_MIRROR_BRUSH_MODEL_COLLISION_325 = 325;
	public const int VER_UE4_CHANGED_CHUNKID_TO_BE_AN_ARRAY_OF_CHUNKIDS_326 = 326;
	public const int VER_UE4_WORLD_NAMED_AFTERARCHIVE_327 = 327;
	public const int VER_UE4_SKY_LIGHT_COMPONENT_328 = 328;
	public const int VER_UE4_WORLD_LAYER_ENABLE_DISTANCE_STREAMING_329 = 329;
	public const int VER_UE4_REMOVE_ZONES_FROM_MODEL_330 = 330;
	public const int VER_UE4_FIX_ANIMATIONBASEPOSE_SERIALIZATION_331 = 331;
	public const int VER_UE4_SUPPORT_8_BONE_INFLUENCES_SKELETAL_MESHES_332 = 332;
	public const int VER_UE4_ADD_OVERRIDE_GRAVITY_FLAG_333 = 333;
	public const int VER_UE4_SUPPORT_GPUSKINNING_8_BONE_INFLUENCES_334 = 334;
	public const int VER_UE4_ANIM_SUPPORT_NONUNIFORM_SCALE_ANIMATION_335 = 335;
	public const int VER_UE4_ENGINE_VERSION_OBJECT_336 = 336;
	public const int VER_UE4_PUBLIC_WORLDS_337 = 337;
	public const int VER_UE4_SKELETON_GUID_SERIALIZATION_338 = 338;
	public const int VER_UE4_CHARACTER_MOVEMENT_WALKABLE_FLOOR_REFACTOR_339 = 339;
	public const int VER_UE4_INVERSE_SQUARED_LIGHTS_DEFAULT_340 = 340;
	public const int VER_UE4_DISABLED_SCRIPT_LIMIT_BYTECODE_341 = 341;
	public const int VER_UE4_PRIVATE_REMOTE_ROLE_342 = 342;
	public const int VER_UE4_FOLIAGE_STATIC_MOBILITY_343 = 343;
	public const int VER_UE4_BUILD_SCALE_VECTOR_344 = 344;
	public const int VER_UE4_FOLIAGE_COLLISION_345 = 345;
	public const int VER_UE4_SKY_BENT_NORMAL_346 = 346;
	public const int VER_UE4_LANDSCAPE_COLLISION_DATA_COOKING_347 = 347;
	public const int VER_UE4_MORPHTARGET_CPU_TANGENTZDELTA_FORMATCHANGE_348 = 348;
	public const int VER_UE4_SOFT_CONSTRAINTS_USE_MASS_349 = 349;
	public const int VER_UE4_REFLECTION_DATA_INARCHIVES_350 = 350;
	public const int VER_UE4_FOLIAGE_MOVABLE_MOBILITY_351 = 351;
	public const int VER_UE4_UNDO_BREAK_MATERIALATTRIBUTES_CHANGE_352 = 352;
	public const int VER_UE4_ADD_CUSTOMPROFILENAME_CHANGE_353 = 353;
	public const int VER_UE4_FLIP_MATERIAL_COORDS_354 = 354;
	public const int VER_UE4_MEMBERREFERENCE_IN_PINTYPE_355 = 355;
	public const int VER_UE4_VEHICLES_UNIT_CHANGE_356 = 356;
	public const int VER_UE4_ANIMATION_REMOVE_NANS_357 = 357;
	public const int VER_UE4_SKELETON_ASSET_PROPERTY_TYPE_CHANGE_358 = 358;
	public const int VER_UE4_FIX_BLUEPRINT_VARIABLE_FLAGS_359 = 359;
	public const int VER_UE4_VEHICLES_UNIT_CHANGE2_360 = 360;
	public const int VER_UE4_UCLASS_SERIALIZE_INTERFACES_AFTER_LINKING_361 = 361;
	public const int VER_UE4_STATIC_MESH_SCREEN_SIZE_LODS_362 = 362;
	public const int VER_UE4_FIX_MATERIAL_COORDS_363 = 363;
	public const int VER_UE4_SPEEDTREE_WIND_V7_364 = 364;
	public const int VER_UE4_LOAD_FOR_EDITOR_GAME_365 = 365;
	public const int VER_UE4_SERIALIZE_RICH_CURVE_KEY_366 = 366;
	public const int VER_UE4_MOVE_LANDSCAPE_MICS_AND_TEXTURES_WITHIN_LEVEL_367 = 367;
	public const int VER_UE4_FTEXT_HISTORY_368 = 368;
	public const int VER_UE4_FIX_MATERIAL_COMMENTS_369 = 369;
	public const int VER_UE4_STORE_BONE_EXPORT_NAMES_370 = 370;
	public const int VER_UE4_MESH_EMITTER_INITIAL_ORIENTATION_DISTRIBUTION_371 = 371;
	public const int VER_UE4_DISALLOW_FOLIAGE_ON_BLUEPRINTS_372 = 372;
	public const int VER_UE4_FIXUP_MOTOR_UNITS_373 = 373;
	public const int VER_UE4_DEPRECATED_MOVEMENTCOMPONENT_MODIFIED_SPEEDS_374 = 374;
	public const int VER_UE4_RENAME_CANBECHARACTERBASE_375 = 375;
	public const int VER_UE4_GAMEPLAY_TAG_CONTAINER_TAG_TYPE_CHANGE_376 = 376;
	public const int VER_UE4_FOLIAGE_SETTINGS_TYPE_377 = 377;
	public const int VER_UE4_STATIC_SHADOW_DEPTH_MAPS_378 = 378;
	public const int VER_UE4_ADD_TRANSACTIONAL_TO_DATA_ASSETS_379 = 379;
	public const int VER_UE4_ADD_LB_WEIGHTBLEND_380 = 380;
	public const int VER_UE4_ADD_ROOTCOMPONENT_TO_FOLIAGEACTOR_381 = 381;
	public const int VER_UE4_FIX_MATERIAL_PROPERTY_OVERRIDE_SERIALIZE_382 = 382;
	public const int VER_UE4_ADD_LINEAR_COLOR_SAMPLER_383 = 383;
	public const int VER_UE4_ADD_STRING_ASSET_REFERENCES_MAP_384 = 384;
	public const int VER_UE4_BLUEPRINT_USE_SCS_ROOTCOMPONENT_SCALE_385 = 385;
	public const int VER_UE4_LEVEL_STREAMING_DRAW_COLOR_TYPE_CHANGE_386 = 386;
	public const int VER_UE4_CLEAR_NOTIFY_TRIGGERS_387 = 387;
	public const int VER_UE4_SKELETON_ADD_SMARTNAMES_388 = 388;
	public const int VER_UE4_ADDED_CURRENCY_CODE_TO_FTEXT_389 = 389;
	public const int VER_UE4_ENUM_CLASS_SUPPORT_390 = 390;
	public const int VER_UE4_FIXUP_WIDGET_ANIMATION_CLASS_391 = 391;
	public const int VER_UE4_SOUND_COMPRESSION_TYPE_ADDED_392 = 392;
	public const int VER_UE4_AUTO_WELDING_393 = 393;
	public const int VER_UE4_RENAME_CROUCHMOVESCHARACTERDOWN_394 = 394;
	public const int VER_UE4_LIGHTMAP_MESH_BUILD_SETTINGS_395 = 395;
	public const int VER_UE4_RENAME_SM3_TO_ES3_1_396 = 396;
	public const int VER_UE4_DEPRECATE_UMG_STYLE_ASSETS_397 = 397;
	public const int VER_UE4_POST_DUPLICATE_NODE_GUID_398 = 398;
	public const int VER_UE4_RENAME_CAMERA_COMPONENT_VIEW_ROTATION_399 = 399;
	public const int VER_UE4_CASE_PRESERVING_FNAME_400 = 400;
	public const int VER_UE4_RENAME_CAMERA_COMPONENT_CONTROL_ROTATION_401 = 401;
	public const int VER_UE4_FIX_REFRACTION_INPUT_MASKING_402 = 402;
	public const int VER_UE4_GLOBAL_EMITTER_SPAWN_RATE_SCALE_403 = 403;
	public const int VER_UE4_CLEAN_DESTRUCTIBLE_SETTINGS_404 = 404;
	public const int VER_UE4_CHARACTER_MOVEMENT_UPPER_IMPACT_BEHAVIOR_405 = 405;
	public const int VER_UE4_BP_MATH_VECTOR_EQUALITY_USES_EPSILON_406 = 406;
	public const int VER_UE4_FOLIAGE_STATIC_LIGHTING_SUPPORT_407 = 407;
	public const int VER_UE4_SLATE_COMPOSITE_FONTS_408 = 408;
	public const int VER_UE4_REMOVE_SAVEGAMESUMMARY_409 = 409;
	public const int VER_UE4_REMOVE_SKELETALMESH_COMPONENT_BODYSETUP_SERIALIZATION_410 = 410;
	public const int VER_UE4_SLATE_BULK_FONT_DATA_411 = 411;
	public const int VER_UE4_ADD_PROJECTILE_FRICTION_BEHAVIOR_412 = 412;
	public const int VER_UE4_MOVEMENTCOMPONENT_AXIS_SETTINGS_413 = 413;
	public const int VER_UE4_GRAPH_INTERACTIVE_COMMENTBUBBLES_414 = 414;
	public const int VER_UE4_LANDSCAPE_SERIALIZE_PHYSICS_MATERIALS_415 = 415;
	public const int VER_UE4_RENAME_WIDGET_VISIBILITY_416 = 416;
	public const int VER_UE4_ANIMATION_ADD_TRACKCURVES_417 = 417;
	public const int VER_UE4_MONTAGE_BRANCHING_POINT_REMOVAL_418 = 418;
	public const int VER_UE4_BLUEPRINT_ENFORCE_CONST_IN_FUNCTION_OVERRIDES_419 = 419;
	public const int VER_UE4_ADD_PIVOT_TO_WIDGET_COMPONENT_420 = 420;
	public const int VER_UE4_PAWN_AUTO_POSSESS_AI_421 = 421;
	public const int VER_UE4_FTEXT_HISTORY_DATE_TIMEZONE_422 = 422;
	public const int VER_UE4_SORT_ACTIVE_BONE_INDICES_423 = 423;
	public const int VER_UE4_PERFRAME_MATERIAL_UNIFORM_EXPRESSIONS_424 = 424;
	public const int VER_UE4_MIKKTSPACE_IS_DEFAULT_425 = 425;
	public const int VER_UE4_LANDSCAPE_GRASS_COOKING_426 = 426;
	public const int VER_UE4_FIX_SKEL_VERT_ORIENT_MESH_PARTICLES_427 = 427;
	public const int VER_UE4_LANDSCAPE_STATIC_SECTION_OFFSET_428 = 428;
	public const int VER_UE4_ADD_MODIFIERS_RUNTIME_GENERATION_429 = 429;
	public const int VER_UE4_MATERIAL_MASKED_BLENDMODE_TIDY_430 = 430;
	public const int VER_UE4_MERGED_ADD_MODIFIERS_RUNTIME_GENERATION_TO_4_7_DEPRECATED_431 = 431;
	public const int VER_UE4_AFTER_MERGED_ADD_MODIFIERS_RUNTIME_GENERATION_TO_4_7_DEPRECATED_432 = 432;
	public const int VER_UE4_MERGED_ADD_MODIFIERS_RUNTIME_GENERATION_TO_4_7_433 = 433;
	public const int VER_UE4_AFTER_MERGING_ADD_MODIFIERS_RUNTIME_GENERATION_TO_4_7_434 = 434;
	public const int VER_UE4_SERIALIZE_LANDSCAPE_GRASS_DATA_435 = 435;
	public const int VER_UE4_OPTIONALLY_CLEAR_GPU_EMITTERS_ON_INIT_436 = 436;
	public const int VER_UE4_SERIALIZE_LANDSCAPE_GRASS_DATA_MATERIAL_GUID_437 = 437;
	public const int VER_UE4_BLUEPRINT_GENERATED_CLASS_COMPONENT_TEMPLATES_PUBLIC_438 = 438;
	public const int VER_UE4_ACTOR_COMPONENT_CREATION_METHOD_439 = 439;
	public const int VER_UE4_K2NODE_EVENT_MEMBER_REFERENCE_440 = 440;
	public const int VER_UE4_STRUCT_GUID_IN_PROPERTY_TAG_441 = 441;
	public const int VER_UE4_REMOVE_UNUSED_UPOLYS_FROM_UMODEL_442 = 442;
	public const int VER_UE4_REBUILD_HIERARCHICAL_INSTANCE_TREES_443 = 443;
	public const int VER_UE4archive_SUMMARY_HAS_COMPATIBLE_ENGINE_VERSION_444 = 444;
	public const int VER_UE4_TRACK_UCS_MODIFIED_PROPERTIES_445 = 445;
	public const int VER_UE4_LANDSCAPE_SPLINE_CROSS_LEVEL_MESHES_446 = 446;
	public const int VER_UE4_DEPRECATE_USER_WIDGET_DESIGN_SIZE_447 = 447;
	public const int VER_UE4_ADD_EDITOR_VIEWS_448 = 448;
	public const int VER_UE4_FOLIAGE_WITH_ASSET_OR_CLASS_449 = 449;
	public const int VER_UE4_BODYINSTANCE_BINARY_SERIALIZATION_450 = 450;
	public const int VER_UE4_SERIALIZE_BLUEPRINT_EVENTGRAPH_FASTCALLS_IN_UFUNCTION_451 = 451;
	public const int VER_UE4_INTERPCURVE_SUPPORTS_LOOPING_452 = 452;
	public const int VER_UE4_MATERIAL_INSTANCE_BASE_PROPERTY_OVERRIDES_DITHERED_LOD_TRANSITION_453 = 453;
	public const int VER_UE4_SERIALIZE_LANDSCAPE_ES2_TEXTURES_454 = 454;
	public const int VER_UE4_CONSTRAINT_INSTANCE_MOTOR_FLAGS_455 = 455;
	public const int VER_UE4_SERIALIZE_PINTYPE_CONST_456 = 456;
	public const int VER_UE4_LIBRARY_CATEGORIES_AS_FTEXT_457 = 457;
	public const int VER_UE4_SKIP_DUPLICATE_EXPORTS_ON_SAVEARCHIVE_458 = 458;
	public const int VER_UE4_SERIALIZE_TEXT_INARCHIVES_459 = 459;
	public const int VER_UE4_ADD_BLEND_MODE_TO_WIDGET_COMPONENT_460 = 460;
	public const int VER_UE4_NEW_LIGHTMASS_PRIMITIVE_SETTING_461 = 461;
	public const int VER_UE4_REPLACE_SPRING_NOZ_PROPERTY_462 = 462;
	public const int VER_UE4_TIGHTLY_PACKED_ENUMS_463 = 463;
	public const int VER_UE4_ASSET_IMPORT_DATA_AS_JSON_464 = 464;
	public const int VER_UE4_TEXTURE_LEGACY_GAMMA_465 = 465;
	public const int VER_UE4_ADDED_NATIVE_SERIALIZATION_FOR_IMMUTABLE_STRUCTURES_466 = 466;
	public const int VER_UE4_DEPRECATE_UMG_STYLE_OVERRIDES_467 = 467;
	public const int VER_UE4_STATIC_SHADOWMAP_PENUMBRA_SIZE_468 = 468;
	public const int VER_UE4_NIAGARA_DATA_OBJECT_DEV_UI_FIX_469 = 469;
	public const int VER_UE4_FIXED_DEFAULT_ORIENTATION_OF_WIDGET_COMPONENT_470 = 470;
	public const int VER_UE4_REMOVED_MATERIAL_USED_WITH_UI_FLAG_471 = 471;
	public const int VER_UE4_CHARACTER_MOVEMENT_ADD_BRAKING_FRICTION_472 = 472;
	public const int VER_UE4_BSP_UNDO_FIX_473 = 473;
	public const int VER_UE4_DYNAMIC_PARAMETER_DEFAULT_VALUE_474 = 474;
	public const int VER_UE4_STATIC_MESH_EXTENDED_BOUNDS_475 = 475;
	public const int VER_UE4_ADDED_NON_LINEAR_TRANSITION_BLENDS_476 = 476;
	public const int VER_UE4_AO_MATERIAL_MASK_477 = 477;
	public const int VER_UE4_NAVIGATION_AGENT_SELECTOR_478 = 478;
	public const int VER_UE4_MESH_PARTICLE_COLLISIONS_CONSIDER_PARTICLE_SIZE_479 = 479;
	public const int VER_UE4_BUILD_MESH_ADJ_BUFFER_FLAG_EXPOSED_480 = 480;
	public const int VER_UE4_MAX_ANGULAR_VELOCITY_DEFAULT_481 = 481;
	public const int VER_UE4_APEX_CLOTH_TESSELLATION_482 = 482;
	public const int VER_UE4_DECAL_SIZE_483 = 483;
	public const int VER_UE4_KEEP_ONLYARCHIVE_NAMES_IN_STRING_ASSET_REFERENCES_MAP_484 = 484;
	public const int VER_UE4_COOKED_ASSETS_IN_EDITOR_SUPPORT_485 = 485;
	public const int VER_UE4_DIALOGUE_WAVE_NAMESPACE_AND_CONTEXT_CHANGES_486 = 486;
	public const int VER_UE4_MAKE_ROT_RENAME_AND_REORDER_487 = 487;
	public const int VER_UE4_K2NODE_VAR_REFERENCEGUIDS_488 = 488;
	public const int VER_UE4_SOUND_CONCURRENCYARCHIVE_489 = 489;
	public const int VER_UE4_USERWIDGET_DEFAULT_FOCUSABLE_FALSE_490 = 490;
	public const int VER_UE4_BLUEPRINT_CUSTOM_EVENT_CONST_INPUT_491 = 491;
	public const int VER_UE4_USE_LOW_PASS_FILTER_FREQ_492 = 492;
	public const int VER_UE4_NO_ANIM_BP_CLASS_IN_GAMEPLAY_CODE_493 = 493;
	public const int VER_UE4_SCS_STORES_ALLNODES_ARRAY_494 = 494;
	public const int VER_UE4_FBX_IMPORT_DATA_RANGE_ENCAPSULATION_495 = 495;
	public const int VER_UE4_CAMERA_COMPONENT_ATTACH_TO_ROOT_496 = 496;
	public const int VER_UE4_INSTANCED_STEREO_UNIFORM_UPDATE_497 = 497;
	public const int VER_UE4_STREAMABLE_TEXTURE_MIN_MAX_DISTANCE_498 = 498;
	public const int VER_UE4_INJECT_BLUEPRINT_STRUCT_PIN_CONVERSION_NODES_499 = 499;
	public const int VER_UE4_INNER_ARRAY_TAG_INFO_500 = 500;
	public const int VER_UE4_FIX_SLOT_NAME_DUPLICATION_501 = 501;
	public const int VER_UE4_STREAMABLE_TEXTURE_AABB_502 = 502;
	public const int VER_UE4_PROPERTY_GUID_IN_PROPERTY_TAG_503 = 503;
	public const int VER_UE4_NAME_HASHES_SERIALIZED_504 = 504;
	public const int VER_UE4_INSTANCED_STEREO_UNIFORM_REFACTOR_505 = 505;
	public const int VER_UE4_COMPRESSED_SHADER_RESOURCES_506 = 506;
	public const int VER_UE4_PRELOAD_DEPENDENCIES_IN_COOKED_EXPORTS_507 = 507;
	public const int VER_UE4_TemplateIndex_IN_COOKED_EXPORTS_508 = 508;
	public const int VER_UE4_PROPERTY_TAG_SET_MAP_SUPPORT_509 = 509;
	public const int VER_UE4_ADDED_SEARCHABLE_NAMES_510 = 510;
	public const int VER_UE4_64BIT_EXPORTMAP_SERIALSIZES_511 = 511;
	public const int VER_UE4_SKYLIGHT_MOBILE_IRRADIANCE_MAP_512 = 512;
	public const int VER_UE4_ADDED_SWEEP_WHILE_WALKING_FLAG_513 = 513;
	public const int VER_UE4_ADDED_SOFT_OBJECT_PATH_514 = 514;
	public const int VER_UE4_POINTLIGHT_SOURCE_ORIENTATION_515 = 515;
	public const int VER_UE4_ADDEDARCHIVE_SUMMARY_LOCALIZATION_ID_516 = 516;
	public const int VER_UE4_FIX_WIDE_STRING_CRC_517 = 517;
	public const int VER_UE4_ADDEDARCHIVE_OWNER_518 = 518;
	public const int VER_UE4_SKINWEIGHT_PROFILE_DATA_LAYOUT_CHANGES_519 = 519;
	public const int VER_UE4_NON_OUTERARCHIVE_IMPORT_520 = 520;
	public const int VER_UE4_ASSETREGISTRY_DEPENDENCYFLAGS_521 = 521;
	public const int VER_UE4_CORRECT_LICENSEE_FLAG_522 = 522;
}

public class ArchiveElement : IDisposable
{
	private Lazy<ArchiveElement[]> _ancestors;
	private Lazy<byte[]> _data;
	private ArchiveReader _reader;

	public ArchiveElement(ArchiveReader reader)
	{
		_reader = reader;

		_data = new(() =>
		{
			var length = EndPosition - StartPosition;
			if (length < 1)
			{
				return Array.Empty<byte>();
			}
			lock (_reader)
			{
				var position = _reader.Position;
				_reader.Seek(StartPosition);
				var bytes = _reader.ReadBytes(Length);
				_reader.Seek(position);
				return bytes;
			}
		});

		_ancestors = new(() => Parent == null ? Array.Empty<ArchiveElement>() : Parent.Ancestors.Prepend(Parent).ToArray());
	}

	public int StartPosition { get; set; }
	public int EndPosition { get; set; }
	public int Length => EndPosition - StartPosition;
	public string Type { get; set; }
	public string Name { get; set; }
	public byte[] Data => _data.Value;

	public object Value { get; set; }
	public bool DescribeValue { get; set; }

	public ArchiveElement Parent { get; set; }
	public List<ArchiveElement> Children { get; } = new();
	public ArchiveElement[] Ancestors => _ancestors.Value;
	public override string ToString() => Value != null ? $"{Name}: {Value}" : Name;

	public void Dispose()
	{
		_reader.EndElement(this);
	}
}
