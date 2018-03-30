using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
[ExecuteInEditMode]
public class Tile3D : MonoBehaviour
{
    // sides of a cube
    public static Vector3[] Faces = new Vector3[6]
    {
        Vector3.up, Vector3.down,
        Vector3.left, Vector3.right,
        Vector3.forward, Vector3.back
    };

    // individual 3d tile (used to create / manage blocks)
    [System.Serializable]
    public class Block
    {
        public Vector3Int Tile;
        public Face[] Faces = new Face[6];
    }

    // Face of a Block
    [System.Serializable]
    public struct Face
    {
        public Vector2Int Tile;
        public int Rotation;
        public bool FlipX;
        public bool FlipY;
        public bool Hidden;

        public static bool operator ==(Face f1, Face f2) { return f1.Equals(f2); }
        public static bool operator !=(Face f1, Face f2) { return !f1.Equals(f2); }

        public override bool Equals(object obj)
        {
            if (obj is Face)
            {
                var other = (Face)obj;
                return Tile == other.Tile && Rotation == other.Rotation && FlipX == other.FlipX && FlipY == other.FlipY && Hidden == other.Hidden;
            }

            return false;
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }

    // used to build the meshes
    // currently we use 2, one for what is actually being rendered and one for collisions (so the editor can click stuff)
    private class MeshBuilder
    {
        public Mesh Mesh;
        
        private List<Vector3> vertices = new List<Vector3>();
        private List<Vector2> uvs = new List<Vector2>();
        private List<int> triangles = new List<int>();
        private bool collider;
        private Vector2 uvTileSize;
        private float tilePadding;
        private float vertexNoise;
        private Vector2[] temp = new Vector2[4];

        public MeshBuilder(bool collider)
        {
            Mesh = new Mesh();
            Mesh.name = (collider ? "Tile3D Editor Collision Mesh" : "Tile3D Render Mesh");
            this.collider = collider;
        }

        public void Begin(Vector2 uvTileSize, float tilePadding)
        {
            this.uvTileSize = uvTileSize;
            this.tilePadding = tilePadding;
            
            vertices.Clear();
            uvs.Clear();
            triangles.Clear();
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Face face)
        {
            if (face.Hidden && !collider)
                return;

            var start = vertices.Count;

            // add Vertices
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);

            // add UVs
            Vector2 uva, uvb, uvc, uvd;
            {
                if (!collider)
                {
                    var center = new Vector2(face.Tile.x + 0.5f, face.Tile.y + 0.5f);
                    var s = 0.5f - tilePadding;
                    var flipx = (face.FlipX ? -1 : 1);
                    var flipy = (face.FlipY ? -1 : 1);

                    temp[0] = new Vector2((center.x + s * flipx) * uvTileSize.x, (center.y - s * flipy) * uvTileSize.y);
                    temp[1] = new Vector2((center.x - s * flipx) * uvTileSize.x, (center.y - s * flipy) * uvTileSize.y);
                    temp[2] = new Vector2((center.x - s * flipx) * uvTileSize.x, (center.y + s * flipy) * uvTileSize.y);
                    temp[3] = new Vector2((center.x + s * flipx) * uvTileSize.x, (center.y + s * flipy) * uvTileSize.y);

                    uva = temp[(face.Rotation + 0) % 4];
                    uvb = temp[(face.Rotation + 1) % 4];
                    uvc = temp[(face.Rotation + 2) % 4];
                    uvd = temp[(face.Rotation + 3) % 4];
                }
                else
                {
                    uva = uvb = uvc = uvd = Vector2.zero;
                }

                uvs.Add(uva);
                uvs.Add(uvb);
                uvs.Add(uvc);
                uvs.Add(uvd);
            }
            
            // Add Triangles
            triangles.Add(start + 0);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 0);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        public void End()
        {
            Mesh.Clear();
            Mesh.vertices = vertices.ToArray();
            Mesh.uv = uvs.ToArray();
            Mesh.triangles = triangles.ToArray();
            Mesh.RecalculateBounds();
            Mesh.RecalculateNormals();
        }
    }

    [HideInInspector]
    public List<Block> Blocks;
    public int TileWidth = 16;
    public int TileHeight = 16;
    public float TilePadding = 0.05f;

    public Texture Texture
    {
        get
        {
            var material = meshRenderer.sharedMaterial;
            if (material != null)
                return material.mainTexture;
            return null;
        }
    }
    public Vector2 UVTileSize
    {
        get
        {
            if (Texture != null)
                return new Vector2(1f / (Texture.width / TileWidth), 1f / (Texture.height / TileHeight));
            return Vector2.one;
        }
    }

    private Dictionary<Vector3Int, Block> map;

    private MeshRenderer meshRenderer;
    private MeshFilter meshFiler;
    private MeshCollider meshCollider;
    private MeshBuilder renderMeshBuilder;
    private MeshBuilder colliderMeshBuilder;

    private void OnEnable()
    {
        meshFiler = GetComponent<MeshFilter>();
        meshRenderer = GetComponent<MeshRenderer>();
        meshCollider = GetComponent<MeshCollider>();
        
        // create initial mesh
        if (renderMeshBuilder == null)
        {
            renderMeshBuilder = new MeshBuilder(false);
            colliderMeshBuilder = new MeshBuilder(true);

            meshFiler.sharedMesh = renderMeshBuilder.Mesh;
            meshCollider.sharedMesh = colliderMeshBuilder.Mesh;
        }

        // reconstruct map
        if (map == null)
        {
            RebuildBlockMap();
        }

        // make initial cells
        if (Blocks == null)
        {
            Blocks = new List<Block>();
            for (int x = -4; x < 4; x++)
                for (int z = -4; z < 4; z++)
                    Create(new Vector3Int(x, 0, z));
        }

        Rebuild();
    }

    public Block Create(Vector3Int at, Vector3Int? from = null)
    {
        Block block;
        if (!map.TryGetValue(at, out block))
        {
            block = new Block();
            block.Tile = at;
            Blocks.Add(block);
            map.Add(at, block);

            if (from != null)
            {
                var before = At(from.Value);
                if (before != null)
                    for (int i = 0; i < Faces.Length; i++)
                        block.Faces[i] = before.Faces[i];
            }
        }

        return block;
    }

    public void Destroy(Vector3Int at)
    {
        Block block;
        if (map.TryGetValue(at, out block))
        {
            map.Remove(at);
            Blocks.Remove(block);
        }
    }

    public Block At(Vector3Int at)
    {
        Block block;
        if (map.TryGetValue(at, out block))
            return block;
        return null;
    }

    public void RebuildBlockMap()
    {
        map = new Dictionary<Vector3Int, Block>();
        if (Blocks != null)
            foreach (var cell in Blocks)
                map.Add(cell.Tile, cell);
    }

    public void Rebuild()
    {
        renderMeshBuilder.Begin(UVTileSize, TilePadding);
        colliderMeshBuilder.Begin(UVTileSize, TilePadding);

        // generate each block
        foreach (var block in Blocks)
        {
            var origin = new Vector3(block.Tile.x + 0.5f, block.Tile.y + 0.5f, block.Tile.z + 0.5f);

            for (int i = 0; i < Faces.Length; i++)
            {
                var normal = new Vector3Int((int)Faces[i].x, (int)Faces[i].y, (int)Faces[i].z);
                if (At(block.Tile + normal) == null)
                    BuildFace(origin, block, normal, block.Faces[i]);
            }
        }

        renderMeshBuilder.End();
        colliderMeshBuilder.End();

        meshFiler.sharedMesh = renderMeshBuilder.Mesh;
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = colliderMeshBuilder.Mesh;
    }

    private void BuildFace(Vector3 center, Block block, Vector3 normal, Face face)
    {
        var up = Vector3.down;
        if (normal.y != 0)
            up = Vector2.left;
        
        var front = center + normal * 0.5f;
        var perp1 = Vector3.Cross(normal, up);
        var perp2 = Vector3.Cross(perp1, normal);

        var a = front + (-perp1 + perp2) * 0.5f;
        var b = front + (perp1 + perp2) * 0.5f;
        var c = front + (perp1 + -perp2) * 0.5f;
        var d = front + (-perp1 + -perp2) * 0.5f;

        renderMeshBuilder.Quad(a, b, c, d, face);
        colliderMeshBuilder.Quad(a, b, c, d, face);
    }

}
