using System;
using System.Windows;
using System.Windows.Media.Media3D;

namespace TuringMorphogenesis3D.Models;

/// <summary>
/// Сетки, на которые «натягивается» плоская текстура паттерна через UV-координаты.
/// Все нормали нормализованы (нужно и для освещения, и для рельефа).
/// </summary>
public static class MeshGenerator
{
    public static MeshGeometry3D Create(MeshType type, int resU, int resV) => type switch
    {
        MeshType.Sphere => CreateSphere(resU, resV, 2.0),
        MeshType.Torus => CreateTorus(resU, resV, 2.0, 0.75),
        MeshType.Plane => CreatePlane(resU, resV, 5.0),
        MeshType.Cylinder => CreateCylinder(resU, resV, 1.5, 4.0),
        _ => CreateSphere(resU, resV, 2.0)
    };

    public static MeshGeometry3D CreateSphere(int resU, int resV, double radius) =>
        Build(resU, resV, (u, v) =>
        {
            double theta = u * 2.0 * Math.PI;
            double phi = v * Math.PI;
            var n = new Vector3D(
                Math.Sin(phi) * Math.Cos(theta),
                Math.Cos(phi),
                Math.Sin(phi) * Math.Sin(theta));
            return (new Point3D(n.X * radius, n.Y * radius, n.Z * radius), n);
        });

    public static MeshGeometry3D CreateTorus(int resU, int resV, double major, double minor) =>
        Build(resU, resV, (u, v) =>
        {
            double theta = u * 2.0 * Math.PI;
            double phi = v * 2.0 * Math.PI;
            double r = major + minor * Math.Cos(phi);
            var p = new Point3D(r * Math.Cos(theta), minor * Math.Sin(phi), r * Math.Sin(theta));
            var n = new Vector3D(Math.Cos(phi) * Math.Cos(theta), Math.Sin(phi), Math.Cos(phi) * Math.Sin(theta));
            return (p, n);
        });

    public static MeshGeometry3D CreatePlane(int resU, int resV, double size) =>
        Build(resU, resV, (u, v) =>
            (new Point3D(u * size - size / 2.0, 0.0, v * size - size / 2.0), new Vector3D(0, 1, 0)));

    public static MeshGeometry3D CreateCylinder(int resU, int resV, double radius, double height) =>
        Build(resU, resV, (u, v) =>
        {
            double theta = u * 2.0 * Math.PI;
            var n = new Vector3D(Math.Cos(theta), 0.0, Math.Sin(theta));
            return (new Point3D(radius * n.X, v * height - height / 2.0, radius * n.Z), n);
        });

    private static MeshGeometry3D Build(int resU, int resV,
        Func<double, double, (Point3D Position, Vector3D Normal)> surface)
    {
        var mesh = new MeshGeometry3D();

        for (int j = 0; j <= resV; j++)
            for (int i = 0; i <= resU; i++)
            {
                double u = (double)i / resU;
                double v = (double)j / resV;
                var (p, n) = surface(u, v);
                n.Normalize();

                mesh.Positions.Add(p);
                mesh.Normals.Add(n);
                mesh.TextureCoordinates.Add(new Point(u, v));
            }

        int stride = resU + 1;
        for (int j = 0; j < resV; j++)
            for (int i = 0; i < resU; i++)
            {
                int tl = j * stride + i;
                int tr = tl + 1;
                int bl = tl + stride;
                int br = bl + 1;

                mesh.TriangleIndices.Add(tl);
                mesh.TriangleIndices.Add(bl);
                mesh.TriangleIndices.Add(tr);

                mesh.TriangleIndices.Add(tr);
                mesh.TriangleIndices.Add(bl);
                mesh.TriangleIndices.Add(br);
            }

        return mesh;
    }
}