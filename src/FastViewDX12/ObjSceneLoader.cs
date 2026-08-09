using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace FastViewDX12;

public static class ObjSceneLoader
{
    private readonly record struct VertexReference(
        int Position,
        int TexCoord,
        int Normal,
        int SmoothingKey);

    private readonly record struct MeshKey(
        string Name,
        int MaterialIndex);

    private sealed class MeshBuilder
    {
        private readonly Dictionary<VertexReference, int> _vertexIndices = new();
        private readonly List<Vector3> _positions = new();
        private readonly List<Vector3> _normals = new();
        private readonly List<Vector2> _texCoords = new();
        private readonly List<int> _indices = new();
        private bool _hasMissingNormal;
        private bool _hasAnyTexCoord;

        public MeshBuilder(string name, int materialIndex)
        {
            Name = name;
            MaterialIndex = materialIndex;
        }

        public string Name { get; }

        public int MaterialIndex { get; }

        public int TriangleCount => _indices.Count / 3;

        public void AddTriangle(
            ParsedVertex a,
            ParsedVertex b,
            ParsedVertex c,
            int smoothingGroup,
            int faceNumber,
            IReadOnlyList<Vector3> sourcePositions,
            IReadOnlyList<Vector2> sourceTexCoords,
            IReadOnlyList<Vector3> sourceNormals)
        {
            AddVertex(a);
            AddVertex(b);
            AddVertex(c);

            void AddVertex(ParsedVertex parsed)
            {
                int smoothingKey = parsed.Normal >= 0
                    ? 0
                    : smoothingGroup > 0
                        ? smoothingGroup
                        : -faceNumber;

                var reference = new VertexReference(
                    parsed.Position,
                    parsed.TexCoord,
                    parsed.Normal,
                    smoothingKey);

                if (!_vertexIndices.TryGetValue(reference, out int index))
                {
                    index = _positions.Count;
                    _vertexIndices.Add(reference, index);
                    _positions.Add(sourcePositions[parsed.Position]);

                    if (parsed.Normal >= 0)
                    {
                        Vector3 normal = sourceNormals[parsed.Normal];
                        _normals.Add(
                            normal.LengthSquared() > 0.00000001f
                                ? Vector3.Normalize(normal)
                                : Vector3.UnitY);
                    }
                    else
                    {
                        _normals.Add(Vector3.Zero);
                        _hasMissingNormal = true;
                    }

                    if (parsed.TexCoord >= 0)
                    {
                        Vector2 uv = sourceTexCoords[parsed.TexCoord];
                        _texCoords.Add(new Vector2(uv.X, 1.0f - uv.Y));
                        _hasAnyTexCoord = true;
                    }
                    else
                    {
                        _texCoords.Add(Vector2.Zero);
                    }
                }

                _indices.Add(index);
            }
        }

        public MeshData Build()
        {
            Vector3[] positions = _positions.ToArray();
            int[] indices = _indices.ToArray();
            Vector3[] normals = _hasMissingNormal
                ? MeshGeometryUtilities.CalculateNormals(positions, indices)
                : _normals.ToArray();
            Vector2[] texCoords = _hasAnyTexCoord
                ? _texCoords.ToArray()
                : Array.Empty<Vector2>();
            Vector4[] tangents = MeshGeometryUtilities.CalculateTangents(
                positions,
                normals,
                texCoords,
                indices);

            return new MeshData
            {
                Name = Name,
                MaterialIndex = MaterialIndex,
                Positions = positions,
                Normals = normals,
                Tangents = tangents,
                TexCoords0 = texCoords,
                Indices = indices
            };
        }
    }

    private readonly record struct ParsedVertex(
        int Position,
        int TexCoord,
        int Normal);

    public static SceneData LoadFromFile(
        string path,
        IProgress<ModelLoadProgress>? progress = null,
        ModelLoadPerformanceReport? performance = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path is empty.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("OBJ file not found.", path);
        }

        string fullPath = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        string fileName = Path.GetFileName(fullPath);
        var result = new SceneData();
        var materialIndices = new Dictionary<string, int>(
            StringComparer.OrdinalIgnoreCase);

        result.Materials.Add(CreateDefaultMaterial());
        materialIndices.Add("DefaultMaterial", 0);

        var sourcePositions = new List<Vector3>();
        var sourceTexCoords = new List<Vector2>();
        var sourceNormals = new List<Vector3>();
        var buildersByKey = new Dictionary<MeshKey, MeshBuilder>();
        var builders = new List<MeshBuilder>();

        string currentName = Path.GetFileNameWithoutExtension(fullPath);
        int currentMaterialIndex = 0;
        int smoothingGroup = 1;
        int faceNumber = 0;
        long fileLength = new FileInfo(fullPath).Length;

        progress?.Report(new ModelLoadProgress(
            ModelLoadStage.ReadingFile,
            "Reading OBJ file",
            fileName));

        using (performance?.Measure("File I/O + OBJ parsing"))
        using (var file = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.SequentialScan))
        using (var reader = new StreamReader(
            file,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024 * 1024))
        {
            string? line;
            int lineNumber = 0;

            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;

                if ((lineNumber & 2047) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int current = fileLength > 0
                        ? (int)Math.Clamp(file.Position * 1000 / fileLength, 0, 1000)
                        : 0;

                    progress?.Report(new ModelLoadProgress(
                        ModelLoadStage.ParsingScene,
                        $"Parsing OBJ geometry (line {lineNumber:N0})",
                        fileName,
                        current,
                        1000));
                }

                ParseLine(
                    line,
                    lineNumber,
                    directory,
                    result,
                    materialIndices,
                    sourcePositions,
                    sourceTexCoords,
                    sourceNormals,
                    buildersByKey,
                    builders,
                    ref currentName,
                    ref currentMaterialIndex,
                    ref smoothingGroup,
                    ref faceNumber);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (builders.Count == 0)
        {
            throw new InvalidDataException("The OBJ file contains no polygon faces.");
        }

        using (performance?.Measure("Mesh/material processing"))
        {
            for (int i = 0; i < builders.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MeshBuilder builder = builders[i];
                result.Meshes.Add(builder.Build());

                progress?.Report(new ModelLoadProgress(
                    ModelLoadStage.ProcessingMeshes,
                    $"Processing meshes {i + 1}/{builders.Count}",
                    fileName,
                    i + 1,
                    builders.Count));

                Debug.WriteLine(
                    $"OBJ mesh '{builder.Name}': " +
                    $"triangles={builder.TriangleCount}, material={builder.MaterialIndex}");
            }
        }

        return result;
    }

    private static void ParseLine(
        string rawLine,
        int lineNumber,
        string directory,
        SceneData scene,
        Dictionary<string, int> materialIndices,
        List<Vector3> positions,
        List<Vector2> texCoords,
        List<Vector3> normals,
        Dictionary<MeshKey, MeshBuilder> buildersByKey,
        List<MeshBuilder> builders,
        ref string currentName,
        ref int currentMaterialIndex,
        ref int smoothingGroup,
        ref int faceNumber)
    {
        string line = StripComment(rawLine).Trim();

        if (line.Length == 0)
        {
            return;
        }

        int separator = FindWhitespace(line);
        string keyword = separator < 0 ? line : line[..separator];
        string value = separator < 0 ? string.Empty : line[(separator + 1)..].Trim();

        switch (keyword)
        {
            case "v":
                positions.Add(ParseVector3(value, "vertex", lineNumber));
                break;

            case "vt":
                texCoords.Add(ParseVector2(value, "texture coordinate", lineNumber));
                break;

            case "vn":
                normals.Add(ParseVector3(value, "normal", lineNumber));
                break;

            case "o":
            case "g":
                if (!string.IsNullOrWhiteSpace(value))
                {
                    currentName = value;
                }
                break;

            case "usemtl":
                currentMaterialIndex = GetOrCreateMaterial(
                    value,
                    scene,
                    materialIndices);
                break;

            case "mtllib":
                LoadMaterialLibraries(
                    value,
                    directory,
                    scene,
                    materialIndices);
                break;

            case "s":
                smoothingGroup = ParseSmoothingGroup(value);
                break;

            case "f":
                faceNumber++;
                AddFace(
                    value,
                    lineNumber,
                    currentName,
                    currentMaterialIndex,
                    smoothingGroup,
                    faceNumber,
                    positions,
                    texCoords,
                    normals,
                    buildersByKey,
                    builders);
                break;
        }
    }

    private static void AddFace(
        string value,
        int lineNumber,
        string meshName,
        int materialIndex,
        int smoothingGroup,
        int faceNumber,
        IReadOnlyList<Vector3> positions,
        IReadOnlyList<Vector2> texCoords,
        IReadOnlyList<Vector3> normals,
        Dictionary<MeshKey, MeshBuilder> buildersByKey,
        List<MeshBuilder> builders)
    {
        string[] tokens = SplitWhitespace(value);

        if (tokens.Length < 3)
        {
            throw new InvalidDataException(
                $"OBJ line {lineNumber}: a face needs at least three vertices.");
        }

        var vertices = new ParsedVertex[tokens.Length];

        for (int i = 0; i < tokens.Length; i++)
        {
            vertices[i] = ParseVertexReference(
                tokens[i],
                positions.Count,
                texCoords.Count,
                normals.Count,
                lineNumber);
        }

        var key = new MeshKey(meshName, materialIndex);

        if (!buildersByKey.TryGetValue(key, out MeshBuilder? builder))
        {
            builder = new MeshBuilder(meshName, materialIndex);
            buildersByKey.Add(key, builder);
            builders.Add(builder);
        }

        for (int i = 1; i + 1 < vertices.Length; i++)
        {
            builder.AddTriangle(
                vertices[0],
                vertices[i],
                vertices[i + 1],
                smoothingGroup,
                faceNumber,
                positions,
                texCoords,
                normals);
        }
    }

    private static ParsedVertex ParseVertexReference(
        string token,
        int positionCount,
        int texCoordCount,
        int normalCount,
        int lineNumber)
    {
        string[] parts = token.Split('/');

        if (parts.Length is < 1 or > 3 || string.IsNullOrWhiteSpace(parts[0]))
        {
            throw new InvalidDataException(
                $"OBJ line {lineNumber}: invalid face vertex '{token}'.");
        }

        int position = ResolveIndex(parts[0], positionCount, "position", lineNumber);
        int texCoord = parts.Length > 1 && parts[1].Length > 0
            ? ResolveIndex(parts[1], texCoordCount, "texture coordinate", lineNumber)
            : -1;
        int normal = parts.Length > 2 && parts[2].Length > 0
            ? ResolveIndex(parts[2], normalCount, "normal", lineNumber)
            : -1;

        return new ParsedVertex(position, texCoord, normal);
    }

    private static int ResolveIndex(
        string text,
        int count,
        string kind,
        int lineNumber)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ||
            value == 0)
        {
            throw new InvalidDataException(
                $"OBJ line {lineNumber}: invalid {kind} index '{text}'.");
        }

        int index = value > 0 ? value - 1 : count + value;

        if (index < 0 || index >= count)
        {
            throw new InvalidDataException(
                $"OBJ line {lineNumber}: {kind} index {value} is out of range.");
        }

        return index;
    }

    private static void LoadMaterialLibraries(
        string value,
        string objDirectory,
        SceneData scene,
        Dictionary<string, int> materialIndices)
    {
        foreach (string path in ResolveLibraryPaths(value, objDirectory))
        {
            LoadMaterialLibrary(path, scene, materialIndices);
        }
    }

    private static IEnumerable<string> ResolveLibraryPaths(
        string value,
        string directory)
    {
        string unquoted = Unquote(value);
        string combined = Path.GetFullPath(Path.Combine(directory, unquoted));

        if (File.Exists(combined))
        {
            yield return combined;
            yield break;
        }

        foreach (string token in SplitWhitespace(value))
        {
            string candidate = Path.GetFullPath(
                Path.Combine(directory, Unquote(token)));

            if (File.Exists(candidate))
            {
                yield return candidate;
            }
        }
    }

    private static void LoadMaterialLibrary(
        string path,
        SceneData scene,
        Dictionary<string, int> materialIndices)
    {
        string directory = Path.GetDirectoryName(path) ?? string.Empty;
        MaterialData? current = null;

        foreach (string rawLine in File.ReadLines(path))
        {
            string line = StripComment(rawLine).Trim();

            if (line.Length == 0)
            {
                continue;
            }

            int separator = FindWhitespace(line);
            string keyword = separator < 0 ? line : line[..separator];
            string value = separator < 0 ? string.Empty : line[(separator + 1)..].Trim();

            if (keyword.Equals("newmtl", StringComparison.OrdinalIgnoreCase))
            {
                int index = GetOrCreateMaterial(value, scene, materialIndices);
                current = scene.Materials[index];
                continue;
            }

            if (current == null)
            {
                continue;
            }

            switch (keyword.ToLowerInvariant())
            {
                case "kd":
                    Vector3 diffuse = ParseVector3(value, "diffuse color", 0);
                    current.BaseColorFactor = new Vector4(diffuse, current.BaseColorFactor.W);
                    break;

                case "ke":
                    Vector3 emissive = ParseVector3(value, "emissive color", 0);
                    current.EmissiveFactor = new Vector4(emissive, 1.0f);
                    break;

                case "d":
                    SetOpacity(current, ParseFloat(FirstToken(value), "opacity", 0));
                    break;

                case "tr":
                    SetOpacity(current, 1.0f - ParseFloat(FirstToken(value), "transparency", 0));
                    break;

                case "ns":
                    float shininess = Math.Max(0.0f, ParseFloat(FirstToken(value), "shininess", 0));
                    current.RoughnessFactor = Math.Clamp(
                        MathF.Sqrt(2.0f / (shininess + 2.0f)),
                        0.04f,
                        1.0f);
                    break;

                case "pr":
                    current.RoughnessFactor = Math.Clamp(
                        ParseFloat(FirstToken(value), "roughness", 0),
                        0.0f,
                        1.0f);
                    break;

                case "pm":
                    current.MetallicFactor = Math.Clamp(
                        ParseFloat(FirstToken(value), "metallic", 0),
                        0.0f,
                        1.0f);
                    break;

                case "map_kd":
                    current.BaseColorTextureBytes = ReadTexture(value, directory);
                    break;

                case "map_ke":
                    current.EmissiveTextureBytes = ReadTexture(value, directory);
                    break;

                case "map_bump":
                case "bump":
                case "norm":
                    current.NormalTextureBytes = ReadTexture(value, directory);
                    break;
            }
        }
    }

    private static byte[]? ReadTexture(string value, string directory)
    {
        string? path = ResolveTexturePath(value, directory);
        return path == null ? null : File.ReadAllBytes(path);
    }

    private static string? ResolveTexturePath(string value, string directory)
    {
        string[] tokens = SplitWhitespace(value);

        for (int start = 0; start < tokens.Length; start++)
        {
            string relative = Unquote(string.Join(' ', tokens[start..]));
            string candidate;

            try
            {
                candidate = Path.GetFullPath(Path.Combine(directory, relative));
            }
            catch
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        Debug.WriteLine($"OBJ material texture was not found: {value}");
        return null;
    }

    private static int GetOrCreateMaterial(
        string name,
        SceneData scene,
        Dictionary<string, int> materialIndices)
    {
        name = string.IsNullOrWhiteSpace(name) ? "DefaultMaterial" : name.Trim();

        if (materialIndices.TryGetValue(name, out int index))
        {
            return index;
        }

        index = scene.Materials.Count;
        materialIndices.Add(name, index);
        scene.Materials.Add(new MaterialData
        {
            Name = name,
            MetallicFactor = 0.0f,
            RoughnessFactor = 0.8f
        });

        return index;
    }

    private static MaterialData CreateDefaultMaterial() =>
        new()
        {
            Name = "DefaultMaterial",
            MetallicFactor = 0.0f,
            RoughnessFactor = 0.8f
        };

    private static void SetOpacity(MaterialData material, float opacity)
    {
        opacity = Math.Clamp(opacity, 0.0f, 1.0f);
        material.BaseColorFactor = new Vector4(
            material.BaseColorFactor.X,
            material.BaseColorFactor.Y,
            material.BaseColorFactor.Z,
            opacity);
        material.AlphaMode = opacity < 0.999f
            ? MeshAlphaMode.Blend
            : MeshAlphaMode.Opaque;
    }

    private static int ParseSmoothingGroup(string value)
    {
        if (value.Equals("off", StringComparison.OrdinalIgnoreCase) || value == "0")
        {
            return 0;
        }

        return int.TryParse(
            FirstToken(value),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int group) && group > 0
                ? group
                : 1;
    }

    private static Vector3 ParseVector3(string value, string kind, int lineNumber)
    {
        string[] parts = SplitWhitespace(value);

        if (parts.Length < 3)
        {
            throw InvalidNumber(kind, lineNumber);
        }

        return new Vector3(
            ParseFloat(parts[0], kind, lineNumber),
            ParseFloat(parts[1], kind, lineNumber),
            ParseFloat(parts[2], kind, lineNumber));
    }

    private static Vector2 ParseVector2(string value, string kind, int lineNumber)
    {
        string[] parts = SplitWhitespace(value);

        if (parts.Length < 2)
        {
            throw InvalidNumber(kind, lineNumber);
        }

        return new Vector2(
            ParseFloat(parts[0], kind, lineNumber),
            ParseFloat(parts[1], kind, lineNumber));
    }

    private static float ParseFloat(string value, string kind, int lineNumber)
    {
        if (!float.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out float result) || !float.IsFinite(result))
        {
            throw InvalidNumber(kind, lineNumber);
        }

        return result;
    }

    private static InvalidDataException InvalidNumber(string kind, int lineNumber)
    {
        string location = lineNumber > 0 ? $" line {lineNumber}" : string.Empty;
        return new InvalidDataException($"Invalid OBJ {kind} value at{location}.");
    }

    private static string StripComment(string value)
    {
        int comment = value.IndexOf('#');
        return comment >= 0 ? value[..comment] : value;
    }

    private static int FindWhitespace(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsWhiteSpace(value[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static string[] SplitWhitespace(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static string FirstToken(string value)
    {
        string[] parts = SplitWhitespace(value);
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    private static string Unquote(string value)
    {
        value = value.Trim();

        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}

