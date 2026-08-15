using Vortice.DXGI;

namespace FastViewDX12;

/// <summary>Lightweight renderer statistics for the optional viewport HUD.</summary>
public sealed partial class Dx12Renderer
{
    public readonly record struct RendererStatistics(
        long VertexCount,
        long TriangleCount,
        ulong VideoMemoryUsage,
        ulong VideoMemoryBudget);

    public RendererStatistics GetStatistics()
    {
        long vertices = 0;
        long triangles = 0;

        if (_scene != null)
        {
            foreach (MeshData mesh in _scene.Meshes)
            {
                vertices += mesh.Positions.LongLength;
                triangles += mesh.Indices.Length > 0
                    ? mesh.Indices.LongLength / 3L
                    : mesh.Positions.LongLength / 3L;
            }
        }

        ulong usage = 0;
        ulong budget = 0;

        try
        {
            if (_adapter != null)
            {
                QueryVideoMemoryInfo memory =
                    _adapter.QueryVideoMemoryInfo(
                        0,
                        MemorySegmentGroup.Local);

                usage = memory.CurrentUsage;
                budget = memory.Budget;
            }
        }
        catch
        {
            // Statistics must never interrupt rendering on older DXGI drivers.
        }

        return new RendererStatistics(vertices, triangles, usage, budget);
    }
}
