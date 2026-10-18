namespace Blitz.SourcePlayer;

public interface IRenderSink
{
    void Texture(string id, int width, int height, byte[] rgba, int mipCount, bool repeat,
        bool linear, string sha256, string origin);
    void Mesh(string id, double[] xyzuv, int[] indices);
    void WorldDraw(string mesh, string texture, string state);
    void Model(int id, string joints);
    void Appearance(int id, string metadata);
    void FreeBall(string mesh, string texture, string origin);
    void Present(double[] viewProjection, double[] joints, int[] models, int[] appearances,
        double[] ball, double[] hudVertices, int[] hudColors, string[] hudTextures, int[] hudCounts, string status);
}
