using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

public class ChessGame : MonoBehaviour
{
    [Header("Board")]
    public GameObject boardPrefab;
    public Camera arCamera;
    public float boardTilt = 30f;

    [Header("AR (Optional)")]
    public ARRaycastManager arRaycastManager;

    [Header("White Pieces")]
    public GameObject whiteKing;
    public GameObject whiteQueen;
    public GameObject whiteRook;
    public GameObject whiteBishop;
    public GameObject whiteKnight;
    public GameObject whitePawn;

    [Header("Black Pieces")]
    public GameObject blackKing;
    public GameObject blackQueen;
    public GameObject blackRook;
    public GameObject blackBishop;
    public GameObject blackKnight;
    public GameObject blackPawn;

    [Header("UI")]
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI movesText;
    public TextMeshProUGUI levelText;

    [Header("Highlight Materials")]
    public Material selectedMaterial;
    public Material validMoveMaterial;

    private GameObject board;
    private enum GameState { WaitingToPlace, Adjusting, Playing }
    private GameState state = GameState.WaitingToPlace;

    private float lastTapTime = 0f;
    private Vector2 touchStartPos;
    private bool isDragging = false;
    private float dragThreshold = 15f;
    
    private Transform[,] squares = new Transform[8, 8];
    private Material[,] originalMaterials = new Material[8, 8];
    private Dictionary<Vector2Int, ChessPiece> piecePositions = new Dictionary<Vector2Int, ChessPiece>();
    private List<GameObject> spawnedPieces = new List<GameObject>();

    private ChessPiece selectedPiece = null;
    private List<Vector2Int> validMoves = new List<Vector2Int>();
    private int movesRemaining;
    private int maxMoves;
    private bool puzzleSolved = false;
    private int currentLevel = 0;
    
    private static List<ARRaycastHit> arHits = new List<ARRaycastHit>();

    void OnEnable() => EnhancedTouchSupport.Enable();
    void OnDisable() => EnhancedTouchSupport.Disable();

    void Start()
    {
        if (arCamera == null) arCamera = Camera.main;
        
        if (arRaycastManager == null)
            arRaycastManager = FindObjectOfType<ARRaycastManager>();

        if (selectedMaterial == null)
        {
            selectedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            selectedMaterial.color = Color.yellow;
        }

        if (validMoveMaterial == null)
        {
            validMoveMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            validMoveMaterial.color = Color.green;
        }

        CheckPrefab("whiteKing", whiteKing);
        CheckPrefab("whiteQueen", whiteQueen);
        CheckPrefab("whiteRook", whiteRook);
        CheckPrefab("whiteBishop", whiteBishop);
        CheckPrefab("whiteKnight", whiteKnight);
        CheckPrefab("whitePawn", whitePawn);
        CheckPrefab("blackKing", blackKing);
        CheckPrefab("blackQueen", blackQueen);
        CheckPrefab("blackRook", blackRook);
        CheckPrefab("blackBishop", blackBishop);
        CheckPrefab("blackKnight", blackKnight);
        CheckPrefab("blackPawn", blackPawn);

        UpdateStatusText("Tap to place board");
        UpdateMovesText("");
        UpdateLevelText("");
    }

    void CheckPrefab(string name, GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogError("PREFAB " + name + ": NULL!");
            return;
        }

        MeshFilter rootMF = prefab.GetComponent<MeshFilter>();
        MeshRenderer rootMR = prefab.GetComponent<MeshRenderer>();

        MeshFilter childMF = prefab.GetComponentInChildren<MeshFilter>();
        MeshRenderer childMR = prefab.GetComponentInChildren<MeshRenderer>();
        
        SkinnedMeshRenderer skinned = prefab.GetComponentInChildren<SkinnedMeshRenderer>();

        string meshInfo = "";
        
        if (rootMF != null && rootMF.sharedMesh != null)
            meshInfo += "RootMesh:" + rootMF.sharedMesh.name + " ";
        else if (rootMF != null)
            meshInfo += "RootMeshFilter:EMPTY ";
            
        if (childMF != null && childMF.sharedMesh != null)
            meshInfo += "ChildMesh:" + childMF.sharedMesh.name + " ";
        else if (childMF != null && childMF != rootMF)
            meshInfo += "ChildMeshFilter:EMPTY ";
            
        if (skinned != null && skinned.sharedMesh != null)
            meshInfo += "SkinnedMesh:" + skinned.sharedMesh.name + " ";
        else if (skinned != null)
            meshInfo += "SkinnedMeshRenderer:EMPTY ";

        if (rootMR != null)
            meshInfo += "RootRenderer:YES ";
        if (childMR != null && childMR != rootMR)
            meshInfo += "ChildRenderer:YES ";

        if (string.IsNullOrEmpty(meshInfo))
            meshInfo = "NO MESH COMPONENTS FOUND!";

        Debug.Log("PREFAB " + name + ": " + prefab.name + " | " + meshInfo);
        
        Debug.Log("  Hierarchy: " + GetHierarchy(prefab.transform, 0));
    }

    string GetHierarchy(Transform t, int depth)
    {
        if (depth > 3) return "...";
        string result = t.name;
        if (t.childCount > 0)
        {
            result += " [";
            for (int i = 0; i < t.childCount; i++)
            {
                if (i > 0) result += ", ";
                result += GetHierarchy(t.GetChild(i), depth + 1);
            }
            result += "]";
        }
        return result;
    }

    void UpdateStatusText(string message)
    {
        if (statusText != null)
            statusText.text = message;
        Debug.Log("Status: " + message);
    }

    void UpdateMovesText(string message)
    {
        if (movesText != null)
            movesText.text = message;
    }

    void UpdateLevelText(string message)
    {
        if (levelText != null)
            levelText.text = message;
    }

    void Update()
    {
        var touches = Touch.activeTouches;
        if (touches.Count == 0) return;

        if (state == GameState.WaitingToPlace)
        {
            if (touches.Count == 1 && touches[0].phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                SpawnBoard(touches[0].screenPosition);
                state = GameState.Adjusting;
                UpdateStatusText("Adjust board, double-tap to start");
            }
        }
        else if (state == GameState.Adjusting)
        {
            if (touches.Count == 1)
            {
                Touch touch = touches[0];
                
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    touchStartPos = touch.screenPosition;
                    isDragging = false;
                    
                    if (Time.time - lastTapTime < 0.3f)
                    {
                        state = GameState.Playing;
                        StartCoroutine(SetupPuzzleCoroutine());
                    }
                    lastTapTime = Time.time;
                }
                else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Moved)
                {
                    float distFromStart = Vector2.Distance(touch.screenPosition, touchStartPos);
                    if (distFromStart > dragThreshold)
                        isDragging = true;
                    
                    if (isDragging && board != null)
                    {
                        Vector2 delta = touch.delta;
                        board.transform.position += new Vector3(delta.x, 0, delta.y) * 0.0005f;
                    }
                }
            }
            else if (touches.Count == 2)
            {
                PinchAndRotate(touches[0], touches[1]);
            }
        }
        else if (state == GameState.Playing)
        {
            if (touches.Count == 1 && touches[0].phase == UnityEngine.InputSystem.TouchPhase.Began)
            {
                HandleGameTap(touches[0].screenPosition);
            }
        }
    }

    void SpawnBoard(Vector2 screenPosition)
    {
        Vector3 spawnPos;
        
        if (arRaycastManager != null && arRaycastManager.Raycast(screenPosition, arHits, TrackableType.PlaneWithinPolygon))
        {
            spawnPos = arHits[0].pose.position;
            Debug.Log("Board placed on AR plane at: " + spawnPos);
        }
        else
        {
            Ray ray = arCamera.ScreenPointToRay(screenPosition);
            spawnPos = ray.origin + ray.direction * 0.5f;
            spawnPos.y = arCamera.transform.position.y - 0.3f;
            Debug.Log("Board placed via raycast at: " + spawnPos);
        }

        board = Instantiate(boardPrefab, spawnPos, Quaternion.identity);
        
        foreach (Rigidbody rb in board.GetComponentsInChildren<Rigidbody>(true))
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        
        Vector3 toCamera = arCamera.transform.position - spawnPos;
        toCamera.y = 0;
        toCamera.Normalize();
        
        board.transform.rotation = Quaternion.LookRotation(-toCamera, Vector3.up);
        board.transform.Rotate(boardTilt - 90, 0, 0, Space.Self);
    }

    void PinchAndRotate(Touch t0, Touch t1)
    {
        if (board == null) return;
        
        Vector2 prev0 = t0.screenPosition - t0.delta;
        Vector2 prev1 = t1.screenPosition - t1.delta;

        float prevDist = Vector2.Distance(prev0, prev1);
        float currDist = Vector2.Distance(t0.screenPosition, t1.screenPosition);
        float scaleDelta = (currDist - prevDist) * 0.0001f;

        Vector3 newScale = board.transform.localScale + Vector3.one * scaleDelta;
        newScale = Vector3.Max(newScale, Vector3.one * 0.01f);
        newScale = Vector3.Min(newScale, Vector3.one * 2f);
        board.transform.localScale = newScale;

        float prevAngle = Mathf.Atan2(prev1.y - prev0.y, prev1.x - prev0.x);
        float currAngle = Mathf.Atan2(t1.screenPosition.y - t0.screenPosition.y, t1.screenPosition.x - t0.screenPosition.x);
        float angleDelta = (currAngle - prevAngle) * Mathf.Rad2Deg;

        board.transform.Rotate(0, -angleDelta, 0, Space.World);
    }

    IEnumerator SetupPuzzleCoroutine()
    {
        ClearAllPieces();
        puzzleSolved = false;

        UpdateStatusText("Setting up puzzle...");
        UpdateLevelText("Level " + (currentLevel + 1));
        yield return null;

        Debug.Log("BOARD: " + board.name + " children=" + board.transform.childCount);

        for (int r = 1; r <= 8; r++)
        {
            Transform row = board.transform.Find("Row (" + r + ")");
            if (row == null) continue;

            for (int c = 1; c <= 8; c++)
            {
                Transform cube = row.Find("Cube (" + c + ")");
                if (cube == null) continue;

                int col = c - 1;
                int rowIndex = 8 - r;
                squares[col, rowIndex] = cube;

                Renderer rend = cube.GetComponent<Renderer>();
                if (rend != null)
                    originalMaterials[col, rowIndex] = rend.material;
            }
        }

        yield return null;

        int foundSquares = 0;
        for (int c = 0; c < 8; c++)
            for (int r = 0; r < 8; r++)
                if (squares[c, r] != null) foundSquares++;
        
        Debug.Log("SQUARES FOUND: " + foundSquares);

        if (foundSquares == 0)
        {
            UpdateStatusText("Error: No squares found!");
            yield break;
        }

        SetupLevel(currentLevel);

        yield return null;
        
        Debug.Log("TOTAL PIECES SPAWNED: " + spawnedPieces.Count);
        
        if (spawnedPieces.Count > 0)
            UpdateStatusText("WHITE to play - Mate in " + ((maxMoves + 1) / 2) + "!");
        else
            UpdateStatusText("Error: No pieces spawned!");
    }

    void SetupLevel(int level)
    {
        Debug.Log("SETUP LEVEL: " + level);
        
        switch (level)
        {
            case 0:
                maxMoves = 2;
                movesRemaining = maxMoves;
                UpdateMovesText("Moves: " + movesRemaining);
                SpawnPiece(whiteRook, 0, 0, true, PieceType.Rook);
                SpawnPiece(whiteKing, 4, 0, true, PieceType.King);
                SpawnPiece(blackKing, 7, 7, false, PieceType.King);
                SpawnPiece(blackPawn, 6, 6, false, PieceType.Pawn);
                SpawnPiece(blackPawn, 7, 6, false, PieceType.Pawn);
                break;

            case 1:
                maxMoves = 2;
                movesRemaining = maxMoves;
                UpdateMovesText("Moves: " + movesRemaining);
                SpawnPiece(whiteQueen, 0, 0, true, PieceType.Queen);
                SpawnPiece(whiteKing, 5, 5, true, PieceType.King);
                SpawnPiece(blackKing, 7, 7, false, PieceType.King);
                break;

            case 2:
                maxMoves = 2;
                movesRemaining = maxMoves;
                UpdateMovesText("Moves: " + movesRemaining);
                SpawnPiece(whiteRook, 0, 7, true, PieceType.Rook);
                SpawnPiece(whiteBishop, 4, 4, true, PieceType.Bishop);
                SpawnPiece(whiteKing, 5, 0, true, PieceType.King);
                SpawnPiece(blackKing, 7, 5, false, PieceType.King);
                SpawnPiece(blackPawn, 6, 6, false, PieceType.Pawn);
                SpawnPiece(blackPawn, 7, 6, false, PieceType.Pawn);
                break;

            default:
                UpdateStatusText("All puzzles complete! Restarting...");
                currentLevel = 0;
                SetupLevel(0);
                break;
        }
    }

    void ClearAllPieces()
    {
        foreach (GameObject piece in spawnedPieces)
            if (piece != null) Destroy(piece);
        spawnedPieces.Clear();
        piecePositions.Clear();
        ClearSelection();
    }

    void SpawnPiece(GameObject prefab, int col, int row, bool isWhite, PieceType type)
    {
        string color = isWhite ? "White" : "Black";
        
        if (prefab == null)
        {
            Debug.LogError("SPAWN FAIL: " + color + " " + type + " prefab NULL");
            return;
        }

        Transform square = squares[col, row];
        if (square == null)
        {
            Debug.LogError("SPAWN FAIL: square (" + col + "," + row + ") NULL");
            return;
        }

        Renderer cubeRenderer = square.GetComponent<Renderer>();
        if (cubeRenderer == null)
        {
            Debug.LogError("SPAWN FAIL: no renderer at (" + col + "," + row + ")");
            return;
        }

        Bounds cubeBounds = cubeRenderer.bounds;
        Vector3 spawnPos = new Vector3(cubeBounds.center.x, cubeBounds.max.y + 0.01f, cubeBounds.center.z);

        GameObject pieceObj = Instantiate(prefab, spawnPos, Quaternion.identity);
        pieceObj.name = color + "_" + type;
        
        foreach (Renderer r in pieceObj.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
            r.gameObject.SetActive(true);
        }
        
        foreach (Transform t in pieceObj.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.SetActive(true);
        }

        Bounds pieceBounds = new Bounds(pieceObj.transform.position, Vector3.zero);
        bool foundBounds = false;
        
        foreach (Renderer r in pieceObj.GetComponentsInChildren<Renderer>())
        {
            if (foundBounds)
                pieceBounds.Encapsulate(r.bounds);
            else
            {
                pieceBounds = r.bounds;
                foundBounds = true;
            }
        }

        if (foundBounds)
        {
            float pieceWidth = Mathf.Max(pieceBounds.size.x, pieceBounds.size.z);
            Debug.Log("SPAWN: " + pieceObj.name + " bounds=" + pieceBounds.size + " width=" + pieceWidth);
            
            if (pieceWidth > 0.0001f)
            {
                float targetWidth = cubeBounds.size.x * 0.6f;
                float scaleFactor = targetWidth / pieceWidth;
                pieceObj.transform.localScale *= scaleFactor;
                
                pieceObj.transform.position = new Vector3(
                    cubeBounds.center.x,
                    cubeBounds.max.y + 0.005f,
                    cubeBounds.center.z
                );
            }
        }
        else
        {
            Debug.LogWarning("SPAWN WARNING: " + pieceObj.name + " has NO RENDERERS!");
            
            pieceObj.transform.localScale = Vector3.one * 0.01f;
        }

        pieceObj.transform.SetParent(board.transform, true);
        spawnedPieces.Add(pieceObj);

        Collider existingCol = pieceObj.GetComponentInChildren<Collider>();
        if (existingCol == null)
        {
            BoxCollider bc = pieceObj.AddComponent<BoxCollider>();
            bc.size = Vector3.one * 0.02f;
            bc.center = Vector3.up * 0.01f;
        }

        ChessPiece piece = pieceObj.AddComponent<ChessPiece>();
        piece.isWhite = isWhite;
        piece.pieceType = type;
        piece.col = col;
        piece.row = row;

        piecePositions[new Vector2Int(col, row)] = piece;
        
        Debug.Log("SPAWNED OK: " + pieceObj.name + " at (" + col + "," + row + ")");
    }

    void HandleGameTap(Vector2 screenPos)
    {
        if (puzzleSolved) return;

        Ray ray = arCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        GameObject hitObj = hit.collider.gameObject;

        ChessPiece tappedPiece = hitObj.GetComponent<ChessPiece>();
        if (tappedPiece == null)
            tappedPiece = hitObj.GetComponentInParent<ChessPiece>();

        if (tappedPiece != null && tappedPiece.isWhite)
        {
            SelectPiece(tappedPiece);
            return;
        }

        if (selectedPiece != null)
        {
            if (tappedPiece != null && !tappedPiece.isWhite)
            {
                Vector2Int enemyPos = new Vector2Int(tappedPiece.col, tappedPiece.row);
                if (validMoves.Contains(enemyPos))
                {
                    MovePiece(selectedPiece, enemyPos);
                    return;
                }
            }

            Vector2Int? tappedSquare = GetSquareFromHit(hitObj);
            if (tappedSquare.HasValue && validMoves.Contains(tappedSquare.Value))
                MovePiece(selectedPiece, tappedSquare.Value);
            else
                ClearSelection();
        }
    }

    Vector2Int? GetSquareFromHit(GameObject hitObj)
    {
        for (int c = 0; c < 8; c++)
            for (int r = 0; r < 8; r++)
                if (squares[c, r] != null && squares[c, r].gameObject == hitObj)
                    return new Vector2Int(c, r);
        return null;
    }

    void SelectPiece(ChessPiece piece)
    {
        ClearSelection();
        selectedPiece = piece;
        validMoves = GetValidMoves(piece);
        HighlightSquare(piece.col, piece.row, selectedMaterial);
        foreach (Vector2Int move in validMoves)
            HighlightSquare(move.x, move.y, validMoveMaterial);
        UpdateStatusText(piece.pieceType + " selected");
    }

    void ClearSelection()
    {
        if (selectedPiece != null)
            RestoreSquare(selectedPiece.col, selectedPiece.row);
        foreach (Vector2Int move in validMoves)
            RestoreSquare(move.x, move.y);
        selectedPiece = null;
        validMoves.Clear();
    }

    void HighlightSquare(int col, int row, Material mat)
    {
        if (squares[col, row] == null) return;
        Renderer rend = squares[col, row].GetComponent<Renderer>();
        if (rend != null) rend.material = mat;
    }

    void RestoreSquare(int col, int row)
    {
        if (squares[col, row] == null) return;
        Renderer rend = squares[col, row].GetComponent<Renderer>();
        if (rend != null && originalMaterials[col, row] != null)
            rend.material = originalMaterials[col, row];
    }

    List<Vector2Int> GetValidMoves(ChessPiece piece)
    {
        List<Vector2Int> moves = new List<Vector2Int>();

        switch (piece.pieceType)
        {
            case PieceType.King:
                for (int dc = -1; dc <= 1; dc++)
                    for (int dr = -1; dr <= 1; dr++)
                        if (dc != 0 || dr != 0)
                            TryAddMove(moves, piece, piece.col + dc, piece.row + dr);
                break;

            case PieceType.Queen:
                AddLineMoves(moves, piece, 1, 0);
                AddLineMoves(moves, piece, -1, 0);
                AddLineMoves(moves, piece, 0, 1);
                AddLineMoves(moves, piece, 0, -1);
                AddLineMoves(moves, piece, 1, 1);
                AddLineMoves(moves, piece, 1, -1);
                AddLineMoves(moves, piece, -1, 1);
                AddLineMoves(moves, piece, -1, -1);
                break;

            case PieceType.Rook:
                AddLineMoves(moves, piece, 1, 0);
                AddLineMoves(moves, piece, -1, 0);
                AddLineMoves(moves, piece, 0, 1);
                AddLineMoves(moves, piece, 0, -1);
                break;

            case PieceType.Bishop:
                AddLineMoves(moves, piece, 1, 1);
                AddLineMoves(moves, piece, 1, -1);
                AddLineMoves(moves, piece, -1, 1);
                AddLineMoves(moves, piece, -1, -1);
                break;

            case PieceType.Knight:
                TryAddMove(moves, piece, piece.col + 2, piece.row + 1);
                TryAddMove(moves, piece, piece.col + 2, piece.row - 1);
                TryAddMove(moves, piece, piece.col - 2, piece.row + 1);
                TryAddMove(moves, piece, piece.col - 2, piece.row - 1);
                TryAddMove(moves, piece, piece.col + 1, piece.row + 2);
                TryAddMove(moves, piece, piece.col + 1, piece.row - 2);
                TryAddMove(moves, piece, piece.col - 1, piece.row + 2);
                TryAddMove(moves, piece, piece.col - 1, piece.row - 2);
                break;

            case PieceType.Pawn:
                int dir = piece.isWhite ? 1 : -1;
                int newRow = piece.row + dir;
                if (IsInBounds(piece.col, newRow) && !piecePositions.ContainsKey(new Vector2Int(piece.col, newRow)))
                    moves.Add(new Vector2Int(piece.col, newRow));
                TryAddPawnCapture(moves, piece, piece.col - 1, newRow);
                TryAddPawnCapture(moves, piece, piece.col + 1, newRow);
                break;
        }

        return moves;
    }

    void AddLineMoves(List<Vector2Int> moves, ChessPiece piece, int dc, int dr)
    {
        int c = piece.col + dc;
        int r = piece.row + dr;

        while (IsInBounds(c, r))
        {
            Vector2Int pos = new Vector2Int(c, r);
            if (piecePositions.ContainsKey(pos))
            {
                if (piecePositions[pos].isWhite != piece.isWhite)
                    moves.Add(pos);
                break;
            }
            moves.Add(pos);
            c += dc;
            r += dr;
        }
    }

    void TryAddMove(List<Vector2Int> moves, ChessPiece piece, int c, int r)
    {
        if (!IsInBounds(c, r)) return;
        Vector2Int pos = new Vector2Int(c, r);
        if (piecePositions.ContainsKey(pos))
        {
            if (piecePositions[pos].isWhite != piece.isWhite)
                moves.Add(pos);
        }
        else
            moves.Add(pos);
    }

    void TryAddPawnCapture(List<Vector2Int> moves, ChessPiece piece, int c, int r)
    {
        if (!IsInBounds(c, r)) return;
        Vector2Int pos = new Vector2Int(c, r);
        if (piecePositions.ContainsKey(pos) && piecePositions[pos].isWhite != piece.isWhite)
            moves.Add(pos);
    }

    bool IsInBounds(int c, int r) => c >= 0 && c < 8 && r >= 0 && r < 8;

    void MovePiece(ChessPiece piece, Vector2Int target)
    {
        bool capturedKing = false;

        if (piecePositions.ContainsKey(target))
        {
            ChessPiece captured = piecePositions[target];
            piecePositions.Remove(target);
            spawnedPieces.Remove(captured.gameObject);
            Destroy(captured.gameObject);
            if (captured.pieceType == PieceType.King)
                capturedKing = true;
        }

        piecePositions.Remove(new Vector2Int(piece.col, piece.row));
        piece.col = target.x;
        piece.row = target.y;
        piecePositions[target] = piece;

        Transform targetSquare = squares[target.x, target.y];
        Renderer rend = targetSquare.GetComponent<Renderer>();
        Bounds b = rend.bounds;
        piece.transform.position = new Vector3(b.center.x, b.max.y + 0.005f, b.center.z);

        ClearSelection();
        movesRemaining--;
        UpdateMovesText("Moves: " + movesRemaining);

        if (capturedKing || IsCheckmate())
        {
            puzzleSolved = true;
            UpdateStatusText("CHECKMATE! Level complete!");
            StartCoroutine(ResetAfterDelay(3f, true));
            return;
        }

        if (movesRemaining <= 0)
        {
            UpdateStatusText("Out of moves! Try again...");
            StartCoroutine(ResetAfterDelay(2f, false));
            return;
        }

        MakeBlackMove();
    }

    void MakeBlackMove()
    {
        List<ChessPiece> blackPieces = new List<ChessPiece>();
        foreach (var kvp in piecePositions)
            if (!kvp.Value.isWhite)
                blackPieces.Add(kvp.Value);

        if (blackPieces.Count == 0) return;

        foreach (ChessPiece bp in blackPieces)
        {
            List<Vector2Int> moves = GetValidMoves(bp);
            if (moves.Count > 0)
            {
                Vector2Int randomMove = moves[Random.Range(0, moves.Count)];
                ExecuteMove(bp, randomMove);
                UpdateStatusText("Black moved. Your turn!");
                return;
            }
        }

        if (IsKingInCheck(false))
        {
            puzzleSolved = true;
            UpdateStatusText("CHECKMATE! Level complete!");
            StartCoroutine(ResetAfterDelay(3f, true));
        }
        else
        {
            UpdateStatusText("Stalemate! Try again...");
            StartCoroutine(ResetAfterDelay(2f, false));
        }
    }

    void ExecuteMove(ChessPiece piece, Vector2Int target)
    {
        if (piecePositions.ContainsKey(target))
        {
            ChessPiece captured = piecePositions[target];
            piecePositions.Remove(target);
            spawnedPieces.Remove(captured.gameObject);
            Destroy(captured.gameObject);
        }

        piecePositions.Remove(new Vector2Int(piece.col, piece.row));
        piece.col = target.x;
        piece.row = target.y;
        piecePositions[target] = piece;

        Transform targetSquare = squares[target.x, target.y];
        Renderer rend = targetSquare.GetComponent<Renderer>();
        Bounds b = rend.bounds;
        piece.transform.position = new Vector3(b.center.x, b.max.y + 0.005f, b.center.z);
    }

    bool IsCheckmate()
    {
        ChessPiece blackKingPiece = null;
        foreach (var kvp in piecePositions)
            if (!kvp.Value.isWhite && kvp.Value.pieceType == PieceType.King)
            {
                blackKingPiece = kvp.Value;
                break;
            }

        if (blackKingPiece == null) return true;
        if (!IsKingInCheck(false)) return false;

        foreach (var kvp in piecePositions)
            if (!kvp.Value.isWhite)
                if (GetValidMoves(kvp.Value).Count > 0)
                    return false;

        return true;
    }

    bool IsKingInCheck(bool whiteKing)
    {
        Vector2Int kingPos = new Vector2Int(-1, -1);
        foreach (var kvp in piecePositions)
            if (kvp.Value.isWhite == whiteKing && kvp.Value.pieceType == PieceType.King)
            {
                kingPos = kvp.Key;
                break;
            }

        if (kingPos.x < 0) return false;

        foreach (var kvp in piecePositions)
            if (kvp.Value.isWhite != whiteKing)
                if (GetValidMoves(kvp.Value).Contains(kingPos))
                    return true;

        return false;
    }

    IEnumerator ResetAfterDelay(float delay, bool won)
    {
        yield return new WaitForSeconds(delay);
        if (won) currentLevel++;
        StartCoroutine(SetupPuzzleCoroutine());
    }
}

public enum PieceType { King, Queen, Rook, Bishop, Knight, Pawn }

public class ChessPiece : MonoBehaviour
{
    public bool isWhite;
    public PieceType pieceType;
    public int col;
    public int row;
}