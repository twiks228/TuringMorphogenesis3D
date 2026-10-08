using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media.Media3D;

namespace TuringMorphogenesis3D.Helpers;

/// <summary>Экспорт текущего меша с рельефом в Wavefront OBJ.</summary>
public static class ObjExporter
{
    public static void Export(MeshGeometry3D mesh, string path)
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(1024 * 1024);

        sb.AppendLine("# TuringMorphogenesis3D mesh export");

        foreach (var p in mesh.Positions)
            sb.Append("v ").Append(p.X.ToString("F5", ci)).Append(' ')
              .Append(p.Y.ToString("F5", ci)).Append(' ')
              .Append(p.Z.ToString("F5", ci)).AppendLine();

        foreach (var t in mesh.TextureCoordinates)
            sb.Append("vt ").Append(t.X.ToString("F5", ci)).Append(' ')
              .Append(t.Y.ToString("F5", ci)).AppendLine();

        foreach (var n in mesh.Normals)
            sb.Append("vn ").Append(n.X.ToString("F5", ci)).Append(' ')
              .Append(n.Y.ToString("F5", ci)).Append(' ')
              .Append(n.Z.ToString("F5", ci)).AppendLine();

        for (int i = 0; i < mesh.TriangleIndices.Count; i += 3)
        {
            int a = mesh.TriangleIndices[i] + 1;
            int b = mesh.TriangleIndices[i + 1] + 1;
            int c = mesh.TriangleIndices[i + 2] + 1;
            sb.Append("f ").Append(a).Append('/').Append(a).Append('/').Append(a).Append(' ')
              .Append(b).Append('/').Append(b).Append('/').Append(b).Append(' ')
              .Append(c).Append('/').Append(c).Append('/').Append(c).AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
    }
}