using UnityEngine;

// Updated: Adding atmospheric haze cubes for better cell visualization
public class EmergencyChessBoard : MonoBehaviour
{
    private Vector3Int boardDimensions = new Vector3Int(4, 4, 4); // Default to 4x4x4
    private float cellSize = 2.8f; // Each cell is 2.8 units

    void Start()
    {
        Debug.Log("*** EMERGENCY CHESS BOARD STARTING - FIXED COORDINATE SYSTEM ***");

        // Use coroutine to wait for configuration to be applied
        StartCoroutine(WaitForConfigurationAndCreateBoard());
    }

    /// <summary>
    /// Wait for BoardDimensionsManager to be configured before creating board
    /// </summary>
    private System.Collections.IEnumerator WaitForConfigurationAndCreateBoard()
    {
        // Wait one frame to allow SceneController.ApplyConfigurationDelayed to set board size
        Debug.Log("🔍 TRACE EmergencyChessBoard: Waiting one frame for configuration to be applied...");
        yield return null;

        Debug.Log("🔍 TRACE EmergencyChessBoard: Frame wait complete, reading BoardDimensionsManager...");
        Debug.Log($"🔍 TRACE EmergencyChessBoard: BoardDimensionsManager.Instance = {(BoardDimensionsManager.Instance != null ? "EXISTS" : "NULL")}");

        // Now get board dimensions from BoardDimensionsManager
        if (BoardDimensionsManager.Instance != null)
        {
            boardDimensions = BoardDimensionsManager.Instance.GetDimensions();
            Debug.Log($"EmergencyChessBoard: Using board dimensions {boardDimensions.x}x{boardDimensions.y}x{boardDimensions.z} after configuration");
            Debug.Log($"🔍 TRACE EmergencyChessBoard: boardDimensions set to {boardDimensions}");
        }
        else
        {
            Debug.LogWarning("EmergencyChessBoard: BoardDimensionsManager not found, using default 4x4x4");
        }

        Debug.Log($"🔍 TRACE EmergencyChessBoard: About to call CreateSimpleBoard with dimensions {boardDimensions}");
        CreateSimpleBoard();
    }

    void CreateSimpleBoard()
    {
        Debug.Log($"Creating simplified emergency chess board ({boardDimensions.x}x{boardDimensions.y}x{boardDimensions.z})...");

        // Create rotatable parent for the entire board
        GameObject rotationParent = new GameObject("Board Rotator");
        BoardRotator rotator = rotationParent.AddComponent<BoardRotator>();

        // Create a child object for board organization
        GameObject boardParent = new GameObject("Emergency Chess Board");
        boardParent.transform.SetParent(rotationParent.transform);

        // Calculate centering offset: half of total board size in world units
        float halfBoardSize = (boardDimensions.x * cellSize) / 2f;
        Vector3 centerOffset = new Vector3(-halfBoardSize, -halfBoardSize, -halfBoardSize);

        // Create a container for pieces that will rotate with the board
        GameObject piecesContainer = new GameObject("Pieces Container");
        piecesContainer.transform.SetParent(rotationParent.transform);
        piecesContainer.transform.localPosition = centerOffset;
        piecesContainer.transform.localRotation = Quaternion.identity;

        // Create floor planes for all board cells
        for (int x = 0; x < boardDimensions.x; x++)
        {
            for (int z = 0; z < boardDimensions.z; z++)
            {
                for (int y = 0; y < boardDimensions.y; y++)
                {
                    GameObject floorPlane = CreateFloorPlane(x, y, z);
                    floorPlane.transform.SetParent(boardParent.transform);
                }
            }
        }

        // Create essential 3D wireframe structure
        CreateEssentialWireframe(boardParent);

        // Center the board
        boardParent.transform.localPosition = centerOffset;
        rotationParent.transform.position = Vector3.zero;

        int totalCells = boardDimensions.x * boardDimensions.y * boardDimensions.z;
        Debug.Log($"3D emergency chess board created with {totalCells} floor planes and essential wireframe structure");
    }
    
    void CreateEssentialWireframe(GameObject boardParent)
    {
        GameObject wireframeContainer = new GameObject("Essential 3D Wireframe");
        wireframeContainer.transform.SetParent(boardParent.transform);

        // Calculate cell boundary positions dynamically based on board dimensions
        // Each cell is 2.8 units, starting at -1.4 offset
        float[] cellBoundaries = new float[boardDimensions.x + 1];
        for (int i = 0; i <= boardDimensions.x; i++)
        {
            cellBoundaries[i] = (i * cellSize) - (cellSize / 2f);
        }

        int lineCount = 0;

        // 1. Outer boundary box (12 lines)
        float minPos = cellBoundaries[0];
        float maxPos = cellBoundaries[boardDimensions.x];

        // Bottom and top faces (8 lines)
        for (int face = 0; face < 2; face++)
        {
            float yPos = (face == 0) ? minPos : maxPos;
            CreateSimpleLine(wireframeContainer, new Vector3(minPos, yPos, minPos), new Vector3(maxPos, yPos, minPos));
            CreateSimpleLine(wireframeContainer, new Vector3(maxPos, yPos, minPos), new Vector3(maxPos, yPos, maxPos));
            CreateSimpleLine(wireframeContainer, new Vector3(maxPos, yPos, maxPos), new Vector3(minPos, yPos, maxPos));
            CreateSimpleLine(wireframeContainer, new Vector3(minPos, yPos, maxPos), new Vector3(minPos, yPos, minPos));
            lineCount += 4;
        }

        // Vertical edges (4 lines)
        CreateSimpleLine(wireframeContainer, new Vector3(minPos, minPos, minPos), new Vector3(minPos, maxPos, minPos));
        CreateSimpleLine(wireframeContainer, new Vector3(maxPos, minPos, minPos), new Vector3(maxPos, maxPos, minPos));
        CreateSimpleLine(wireframeContainer, new Vector3(maxPos, minPos, maxPos), new Vector3(maxPos, maxPos, maxPos));
        CreateSimpleLine(wireframeContainer, new Vector3(minPos, minPos, maxPos), new Vector3(minPos, maxPos, maxPos));
        lineCount += 4;

        // 2. Interior Y-level dividers (frames at each intermediate Y level)
        for (int level = 1; level < boardDimensions.y; level++)
        {
            float yPos = cellBoundaries[level];
            // Create rectangular frame at each Y level
            CreateSimpleLine(wireframeContainer, new Vector3(minPos, yPos, minPos), new Vector3(maxPos, yPos, minPos));
            CreateSimpleLine(wireframeContainer, new Vector3(maxPos, yPos, minPos), new Vector3(maxPos, yPos, maxPos));
            CreateSimpleLine(wireframeContainer, new Vector3(maxPos, yPos, maxPos), new Vector3(minPos, yPos, maxPos));
            CreateSimpleLine(wireframeContainer, new Vector3(minPos, yPos, maxPos), new Vector3(minPos, yPos, minPos));
            lineCount += 4;
        }

        // 3. Key vertical grid lines for X and Z divisions
        // Vertical lines at X boundaries (skip outer edges)
        for (int i = 1; i < boardDimensions.x; i++)
        {
            float xPos = cellBoundaries[i];
            CreateSimpleLine(wireframeContainer, new Vector3(xPos, minPos, minPos), new Vector3(xPos, maxPos, minPos));
            CreateSimpleLine(wireframeContainer, new Vector3(xPos, minPos, maxPos), new Vector3(xPos, maxPos, maxPos));
            lineCount += 2;
        }

        // Vertical lines at Z boundaries (skip outer edges)
        for (int i = 1; i < boardDimensions.z; i++)
        {
            float zPos = cellBoundaries[i];
            CreateSimpleLine(wireframeContainer, new Vector3(minPos, minPos, zPos), new Vector3(minPos, maxPos, zPos));
            CreateSimpleLine(wireframeContainer, new Vector3(maxPos, minPos, zPos), new Vector3(maxPos, maxPos, zPos));
            lineCount += 2;
        }

        Debug.Log($"Essential 3D wireframe created with {lineCount} lines for {boardDimensions.x}x{boardDimensions.y}x{boardDimensions.z} board");
    }
    
    void CreateSimpleLine(GameObject parent, Vector3 start, Vector3 end)
    {
        // Up-and-down lines are named apart so Customize can hide them (BoardLook)
        GameObject lineObj = new GameObject(Mathf.Abs(start.y - end.y) > 0.01f ? "WireframeLine Vertical" : "WireframeLine");
        lineObj.transform.SetParent(parent.transform);
        
        LineRenderer line = lineObj.AddComponent<LineRenderer>();
        
        // Find shader
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }
        
        Material lineMaterial = new Material(shader);
        lineMaterial.color = new Color(0.8f, 0.8f, 0.8f, 1.0f);
        
        line.material = lineMaterial;
        line.positionCount = 2;
        line.startWidth = 0.05f;
        line.endWidth = 0.05f;
        line.useWorldSpace = false;
        line.enabled = true;
        
        line.SetPosition(0, start);
        line.SetPosition(1, end);
    }
    
    GameObject CreateFloorPlane(int x, int y, int z)
    {
        GameObject floorPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floorPlane.name = $"Floor_{x}_{y}_{z}";

        // Position plane at actual cell bottom
        // Each cell spans 2.8 units, so bottom of cell is at y*2.8 - 1.4
        floorPlane.transform.position = new Vector3(x * 2.8f, (y * 2.8f) - 1.4f, z * 2.8f);

        // Scale plane to fit cell area (planes are 10x10 units by default)
        floorPlane.transform.localScale = new Vector3(0.26f, 1f, 0.26f); // Slightly smaller for clear boundaries

        // Create translucent material
        Renderer renderer = floorPlane.GetComponent<Renderer>();

        // Try URP Unlit first, then Standard, then Diffuse as fallback
        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null)
        {
            unlitShader = Shader.Find("Standard");
            Debug.LogWarning("EmergencyChessBoard: URP Unlit shader not found, using Standard shader");
        }
        if (unlitShader == null)
        {
            unlitShader = Shader.Find("Diffuse");
            Debug.LogWarning("EmergencyChessBoard: Standard shader not found, using Diffuse shader");
        }

        Material floorMaterial = new Material(unlitShader);

        // Create visible checkerboard pattern with 3D alternation (level-aware)
        // Light squares with moderate transparency, dark squares with much higher contrast
        if ((x + y + z) % 2 == 0)
        {
            // Light squares - white with moderate transparency for better visibility
            floorMaterial.color = new Color(1f, 1f, 1f, 0.25f);
        }
        else
        {
            // Dark squares - much darker for high contrast checkerboard pattern
            floorMaterial.color = new Color(0.15f, 0.15f, 0.15f, 0.45f);
        }

        // Set up transparency based on shader type
        if (unlitShader.name.Contains("Universal Render Pipeline"))
        {
            // URP Unlit transparency with depth-aware rendering
            floorMaterial.SetFloat("_Surface", 1); // Transparent surface type
            floorMaterial.SetFloat("_Blend", 0);   // Alpha blend mode
            floorMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            floorMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            floorMaterial.SetInt("_ZWrite", 0);
            floorMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual); // Standard depth test
            floorMaterial.renderQueue = 3000;
            Debug.Log($"EmergencyChessBoard: Created floor plane ({x},{y},{z}) with URP Unlit shader, color={floorMaterial.color}");
        }
        else if (unlitShader.name.Contains("Standard"))
        {
            // Standard shader transparency
            floorMaterial.SetInt("_Mode", 3); // Transparent mode
            floorMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            floorMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            floorMaterial.SetInt("_ZWrite", 0);
            floorMaterial.DisableKeyword("_ALPHATEST_ON");
            floorMaterial.EnableKeyword("_ALPHABLEND_ON");
            floorMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            floorMaterial.renderQueue = 3000;
            Debug.Log($"EmergencyChessBoard: Created floor plane ({x},{y},{z}) with Standard shader, color={floorMaterial.color}");
        }
        else
        {
            // Diffuse/Legacy shader - basic transparency
            floorMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            floorMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            floorMaterial.SetInt("_ZWrite", 0);
            floorMaterial.renderQueue = 3000;
            Debug.Log($"EmergencyChessBoard: Created floor plane ({x},{y},{z}) with fallback shader, color={floorMaterial.color}");
        }

        renderer.material = floorMaterial;

        // Remove collider for mobile-first design - only pieces should be clickable
        Collider planeCollider = floorPlane.GetComponent<Collider>();
        if (planeCollider != null)
        {
            DestroyImmediate(planeCollider);
        }

        return floorPlane;
    }
    
}