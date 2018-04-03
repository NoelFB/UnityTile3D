using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Tile3D))]
public class Tile3DEditor : Editor
{

    public Tile3D tiler { get { return (Tile3D)target; } }
    public Vector3 origin { get { return tiler.transform.position; } }

    // current tool mode
    public enum ToolModes
    {
        Transform,
        Building,
        Painting
    }
    private ToolModes toolMode = ToolModes.Building;

    // painting modes
    public enum PaintModes
    {
        Brush,
        Fill
    }
    private PaintModes paintMode = PaintModes.Brush;

    // used to describe a selection (tile + face)
    private class SingleSelection
    {
        public Vector3Int Tile;
        public Vector3 Face;
    }

    // used to describe multiple selections (tile(s) + face)
    private class MultiSelection
    {
        public List<Vector3Int> Tiles = new List<Vector3Int>();
        public Vector3 Face;
        public MultiSelection() { }
        public MultiSelection(SingleSelection from)
        {
            Tiles.Add(from.Tile);
            Face = from.Face;
        }
    }

    // active selections
    private SingleSelection hover = null;
    private MultiSelection selected = null;
    private Tile3D.Face brush = new Tile3D.Face() { Hidden = true };
    
    private void OnEnable()
    {
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= OnUndoRedo;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        
        if (GUILayout.Button("Rebuild Mesh"))
            tiler.Rebuild();
    }

    protected virtual void OnSceneGUI()
    {
        var e = Event.current;
        var invokeRepaint = false;
        var draggingBlock = false;
        var interacting = (!e.control && !e.alt && e.button == 0);
        
        // overlay gui
        Handles.BeginGUI();
        {
            // mode toolbar
            toolMode = (ToolModes)GUI.Toolbar(new Rect(10, 10, 200, 30), (int)toolMode, new[] { "Move", "Build", "Paint" });
            if (toolMode == ToolModes.Painting)
                selected = null;

            // tileset
            if (toolMode == ToolModes.Painting)
                GUI.Window(0, new Rect(10, 70, 200, 400), PaintingWindow, "Tiles");
        }
        Handles.EndGUI();

        // cancel everything if in "move" mode
        if (toolMode == ToolModes.Transform)
        {
            if (Tools.current == Tool.None)
                Tools.current = Tool.Move;
            return;
        }

        // override default control
        Tools.current = Tool.None;
        HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

        // Selecting & Dragging Blocks
        if (toolMode == ToolModes.Building)
        {
            // drag block in / out
            if (selected != null)
            {
                Handles.color = Color.blue;
                EditorGUI.BeginChangeCheck();

                var start = CenterOfSelection(selected) + selected.Face * 0.5f;
                var pulled = Handles.Slider(start, selected.Face);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(target, "EditMesh");

                    draggingBlock = true;
                    if (hover != null)
                    {
                        hover = null;
                        invokeRepaint = true;
                    }

                    // get distance and direction
                    var distance = (pulled - start).magnitude;
                    var outwards = (int)Mathf.Sign(Vector3.Dot(pulled - start, selected.Face));

                    // create or destroy a block (depending on direction)
                    if (distance > 1f)
                    {
                        var newTiles = new List<Vector3Int>();
                        foreach (var tile in selected.Tiles)
                        {
                            var was = tile;
                            var next = tile + selected.Face.Int() * outwards;

                            if (outwards > 0)
                                tiler.Create(next, was);
                            else
                                tiler.Destroy(was);
                            tiler.Rebuild();

                            newTiles.Add(next);
                        }

                        selected.Tiles = newTiles;
                        tiler.Rebuild();
                    }
                }
            }

            // select tiles
            if (!draggingBlock && interacting)
            {
                if (e.type == EventType.MouseDown && !e.shift)
                {
                    if (hover == null)
                        selected = null;
                    else
                        selected = new MultiSelection(hover);
                    invokeRepaint = true;
                }
                else if (e.type == EventType.MouseDrag && selected != null && hover != null && !selected.Tiles.Contains(hover.Tile))
                {
                    selected.Tiles.Add(hover.Tile);
                    invokeRepaint = true;
                }
            }
        }
        
        // active hover
        if ((e.type == EventType.MouseMove || e.type == EventType.MouseDrag) && interacting && !draggingBlock)
        {
            var next = GetSelectionAt(e.mousePosition);
            if ((hover == null && next != null) || (hover != null && next == null) || (hover != null && next != null && (hover.Tile != next.Tile || hover.Face != next.Face)))
                invokeRepaint = true;
            hover = next;
        }
        
        // painting
        if (toolMode == ToolModes.Painting && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && interacting && hover != null)
        {
            var block = tiler.At(hover.Tile);
            if (block != null)
            {
                // paint single tile
                if (paintMode == PaintModes.Brush)
                {
                    if (SetBlockFace(block, hover.Face, brush))
                        tiler.Rebuild();
                }
                // paint bucket
                else if (paintMode == PaintModes.Fill)
                {
                    var face = GetBlockFace(block, hover.Face);
                    if (FillBlockFace(block, face))
                        tiler.Rebuild();
                }
            }
        }

        // right-click to rotate face
        if (toolMode == ToolModes.Painting && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 1 && !e.control && !e.alt && hover != null)
        {
            brush.Rotation = (brush.Rotation + 1) % 4;

            var cell = tiler.At(hover.Tile);
            if (cell != null && SetBlockFace(cell, hover.Face, brush))
                tiler.Rebuild();
        }

        // Drawing
        {
            // draw hovers / selected outlines
            if (hover != null)
                DrawSelection(hover, Color.magenta);
            if (selected != null)
                DrawSelection(selected, Color.blue);

            // force repaint
            if (invokeRepaint)
                Repaint();
        }
        
        // always keep the tiler selected for now
        // later should detect if something is being grabbed or hovered
        Selection.activeGameObject = tiler.transform.gameObject;
    }

    private bool SetBlockFace(Tile3D.Block block, Vector3 normal, Tile3D.Face brush)
    {
        Undo.RecordObject(target, "SetBlockFaces");

        for (int i = 0; i < Tile3D.Faces.Length; i++)
        {
            if (Vector3.Dot(normal, Tile3D.Faces[i]) > 0.8f)
            {
                if (!brush.Hidden)
                {
                    if (brush != block.Faces[i])
                    {
                        block.Faces[i] = brush;
                        return true;
                    }
                }
                else if (!block.Faces[i].Hidden)
                {
                    block.Faces[i].Hidden = true;
                    return true;
                }
            }
        }

        return false;
    }

    private Tile3D.Face GetBlockFace(Tile3D.Block block, Vector3 face)
    {
        for (int i = 0; i < Tile3D.Faces.Length; i++)
        {
            if (Vector3.Dot(face, Tile3D.Faces[i]) > 0.8f)
                return block.Faces[i];
        }

        return block.Faces[0];
    }

    private bool FillBlockFace(Tile3D.Block block, Tile3D.Face face)
    {
        Vector3Int perp1, perp2;
        GetPerpendiculars(hover.Face, out perp1, out perp2);

        var active = new List<Tile3D.Block>();
        var filled = new HashSet<Tile3D.Block>();
        var directions = new Vector3Int[4] { perp1, perp1 * -1, perp2, perp2 * -1 };
        var outwards = hover.Face.Int();
        var changed = false;

        filled.Add(block);
        active.Add(block);
        SetBlockFace(block, hover.Face, brush);

        while (active.Count > 0)
        {
            var from = active[0];
            active.RemoveAt(0);

            for (int i = 0; i < 4; i++)
            {
                var next = tiler.At(from.Tile + directions[i]);
                if (next != null && !filled.Contains(next) && tiler.At(from.Tile + directions[i] + outwards) == null && GetBlockFace(next, hover.Face).Tile == face.Tile)
                {
                    filled.Add(next);
                    active.Add(next);
                    if (SetBlockFace(next, hover.Face, brush))
                        changed = true;
                }
            }
        }

        return changed;
    }

    private Vector3 CenterOfSelection(Vector3Int tile)
    {
        return origin + new Vector3(tile.x + 0.5f, tile.y + 0.5f, tile.z + 0.5f);
    }

    private Vector3 CenterOfSelection(SingleSelection selection)
    {
        return CenterOfSelection(selection.Tile);
    }

    private Vector3 CenterOfSelection(MultiSelection selection)
    {
        var tile = Vector3.zero;
        foreach (var t in selection.Tiles)
            tile += new Vector3(t.x + 0.5f, t.y + 0.5f, t.z + 0.5f);
        tile /= selection.Tiles.Count;
        tile += origin;

        return tile;
    }

    private void DrawSelection(SingleSelection selection, Color color)
    {
        var center = CenterOfSelection(selection);
        DrawSelection(center, selection.Face, color);
    }

    private void DrawSelection(MultiSelection selection, Color color)
    {
        foreach (var tile in selection.Tiles)
            DrawSelection(CenterOfSelection(tile), selection.Face, color);
    }

    private void DrawSelection(Vector3 center, Vector3 face, Color color)
    {
        var front = center + face * 0.5f;
        Vector3 perp1, perp2;
        GetPerpendiculars(face, out perp1, out perp2);

        var a = front + (-perp1 + perp2) * 0.5f;
        var b = front + (perp1 + perp2) * 0.5f;
        var c = front + (perp1 + -perp2) * 0.5f;
        var d = front + (-perp1 + -perp2) * 0.5f;

        Handles.color = color;
        Handles.DrawDottedLine(a, b, 2f);
        Handles.DrawDottedLine(b, c, 2f);
        Handles.DrawDottedLine(c, d, 2f);
        Handles.DrawDottedLine(d, a, 2f);
    }

    private void GetPerpendiculars(Vector3 face, out Vector3 updown, out Vector3 leftright)
    {
        var up = (face.y == 0 ? Vector3.up : Vector3.right);
        updown = Vector3.Cross(face, up);
        leftright = Vector3.Cross(updown, face);
    }

    private void GetPerpendiculars(Vector3 face, out Vector3Int updown, out Vector3Int leftright)
    {
        Vector3 perp1, perp2;
        GetPerpendiculars(face, out perp1, out perp2);
        updown = perp1.Int();
        leftright = perp2.Int();
    }

    private SingleSelection GetSelectionAt(Vector2 mousePosition)
    {
        var ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        var hits = Physics.RaycastAll(ray);

        foreach (var hit in hits)
        {
            var other = hit.collider.gameObject.GetComponent<Tile3D>();
            if (other == tiler)
            {
                var center = hit.point - hit.normal * 0.5f;

                return new SingleSelection()
                {
                    Tile = (center - origin).Floor(),
                    Face = hit.normal
                };
            }
        }

        return null;
    }
    
    // this should probably be in its own file somehow? not sure
    void PaintingWindow(int id)
    {
        const int left = 10;
        const int width = 180;

        // paint mode
        paintMode = (PaintModes)GUI.Toolbar(new Rect(left, 25, width, 30), (int)paintMode, new[] { "Brush", "Fill" });
        brush.Rotation = GUI.Toolbar(new Rect(left + 50, 65, 130, 20), brush.Rotation, new[] { "0", "90", "180", "270" });
        brush.FlipX = GUI.Toggle(new Rect(left + 50, 90, 90, 20), brush.FlipX, "FLIP X");
        brush.FlipY = GUI.Toggle(new Rect(left + 115, 90, 90, 20), brush.FlipY, "FLIP Y");

        // empty tile
        if (DrawPaletteTile(new Rect(left, 65, 40, 40), null, brush.Hidden))
            brush.Hidden = true;

        // tiles
        if (tiler.Texture == null)
        {
            GUI.Label(new Rect(left, 120, width, 80), "Requires a Material\nwith a Texture");
        }
        else
        {
            var columns = tiler.Texture.width / tiler.TileWidth;
            var rows = tiler.Texture.height / tiler.TileHeight;
            var tileWidth = width / columns;
            var tileHeight = (tiler.TileHeight / (float)tiler.TileWidth) * tileWidth;

            for (int x = 0; x < columns; x++)
            {
                for (int y = 0; y < rows; y ++)
                {
                    var rect = new Rect(left + x * tileWidth, 120 + y * tileHeight, tileWidth, tileHeight);
                    var tile = new Vector2Int(x, rows - 1 - y);
                    if (DrawPaletteTile(rect, tile, brush.Tile == tile && !brush.Hidden))
                    {
                        brush.Tile = tile;
                        brush.Hidden = false;
                    }
                }
            }
        }
        
        // repaint
        var e = Event.current;
        if (e.type == EventType.MouseMove || e.type == EventType.MouseDown)
            Repaint();
    }

    bool DrawPaletteTile(Rect rect, Vector2Int? tile, bool selected)
    {
        var e = Event.current;
        var hover = !selected && e.mousePosition.x > rect.x && e.mousePosition.y > rect.y && e.mousePosition.x < rect.xMax && e.mousePosition.y < rect.yMax;
        var pressed = hover && e.type == EventType.MouseDown && e.button == 0;

        // hover
        if (hover)
            EditorGUI.DrawRect(rect, Color.yellow);
        // selected
        else if (selected)
            EditorGUI.DrawRect(rect, Color.blue);

        // tile
        if (tile.HasValue)
        {
            var coords = new Rect(tile.Value.x * tiler.UVTileSize.x, tile.Value.y * tiler.UVTileSize.y, tiler.UVTileSize.x, tiler.UVTileSize.y);
            GUI.DrawTextureWithTexCoords(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height - 4), tiler.Texture, coords);
        }
        else
        {
            EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 2, rect.width - 4, rect.height- 4), Color.white);
            EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 2, (rect.width - 4) / 2, (rect.height - 4) / 2), Color.gray);
            EditorGUI.DrawRect(new Rect(rect.x + 2 + (rect.width - 4) / 2, rect.y + 2 + (rect.height - 4) / 2, (rect.width - 4) / 2, (rect.height - 4) / 2), Color.gray);
        }

        if (pressed)
            e.Use();

        return pressed;
    }

    void OnUndoRedo()
    {
        var tar = target as Tile3D;
        selected = null;
        hover = null;
        // After an undo the underlying block dictionary is out of sync with
        // the blocks list. Blocks have been removed, dictionary hasn't been
        // updated yet which causes artifacts during rebuild. So - update it.
        tar.RebuildBlockMap();
        tar.Rebuild();
    }
}
