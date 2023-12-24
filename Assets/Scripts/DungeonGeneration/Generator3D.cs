using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;
using Graphs;
using UnityEditor;
using Cinemachine;

public class Generator3D : MonoBehaviour {
    enum CellType {
        None,
        Room,
        Hallway,
        Vent
    }
    class Cell {
        public CellType celltype = CellType.None;
        public int hallConfig = 63;
        public GameObject[] hallSides = {
            null,
            null,
            null,
            null,
            null,
            null
        };

        public Cell () { }
        public Cell (CellType type, int hallConf = 63) {
            celltype = type;
            hallConfig = hallConf;
        }
        public static Vector3Int[] neighbors = {
            new Vector3Int(-1, 0, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 0, -1),

            new Vector3Int(0, 1, 0),
            new Vector3Int(0, -1, 0),
        };
        public static Vector3Int[] wallPos = {
            new Vector3Int(0, 1, 0),
            new Vector3Int(0, 0, 1),
            new Vector3Int(1, 0, 0),
            new Vector3Int(0, 1, 0),

            new Vector3Int(1, 1, 1),
            new Vector3Int(0, 0, 0),
        };
        public static Quaternion[] wallRot = {
            Quaternion.Euler(0, 0, -90),
            Quaternion.Euler(-90, 0, 0),
            Quaternion.Euler(0, 0, 90),
            Quaternion.Euler(90, 0, 0),

            Quaternion.Euler(0, -90, -180),
            Quaternion.Euler(0, 0, 0),
        };
        public void generateWalls (Vector3Int location, GameObject wallPrefab, int levelHeight) {
            for (int i = 0; i < 6; i++) {
                bool wallExists = ((hallConfig & (1 << i)) != 0);
                if (!wallExists) {
                    if (hallSides[i] != null)
                        Destroy (hallSides[i]);
                    continue;
                }
                if (hallSides[i] == null /* && i != 4*/) {
                    hallSides[i] = Instantiate (wallPrefab, location + wallPos[i], wallRot[i]);
                    SetGameLayerRecursive (hallSides[i], LayerMask.NameToLayer ("floor" + (location.y / levelHeight)));
                }
            }
        }
    }

    class Room {
        public BoundsInt bounds;
        public int index;

        public Room(Vector3Int location, Vector3Int size, int _ind) {
            bounds = new BoundsInt(location, size);
            index = _ind;
        }

        public static bool Intersect(Room a, Room b) {
            return !((a.bounds.position.x >= (b.bounds.position.x + b.bounds.size.x)) || ((a.bounds.position.x + a.bounds.size.x) <= b.bounds.position.x)
                || (a.bounds.position.y >= (b.bounds.position.y + b.bounds.size.y)) || ((a.bounds.position.y + a.bounds.size.y) <= b.bounds.position.y)
                || (a.bounds.position.z >= (b.bounds.position.z + b.bounds.size.z)) || ((a.bounds.position.z + a.bounds.size.z) <= b.bounds.position.z));
        }
    }

    [Range(1, 20)]  public int floorsCount = 1;
                    public int levelHeight = 5;

    [SerializeField] Vector3Int size;
    [SerializeField] int roomCount;
    [SerializeField] int hallWidth = 3;
    [SerializeField] int ventWidth = 1;
    [SerializeField] [Range(0f, 1f)] float stairsProbability = 0.33f;


    [SerializeField] Material blueMaterial;
    [SerializeField] Material greenMaterial;

    [SerializeField] GameObject wallPrefab;
    [SerializeField] GameObject cubePrefab;
    [SerializeField] GameObject[] roomPrefabs;
    [SerializeField] int reservedIndex = 1;

    [SerializeField] GameObject player;
    [SerializeField] CinemachineVirtualCamera cam;

    Grid3D<Cell> grid;
    Random random;

    List<List<Room>> roomsTotal;
    HashSet<Prim.Edge> edgesTotal;

    Vector3Int[] hallNeighbors {
        get { return getHallNeighborsList (hallWidth); }
    }
    Vector3Int[] ventNeighbors {
        get { return getHallNeighborsList (ventWidth); }
    }

    void Start() {
        random = new Random(0);
        roomsTotal = new List<List<Room>> ();
        edgesTotal = new HashSet<Prim.Edge>();

        InitGrid();
        Generate();
    }

    private void InitGrid() {
        grid = new Grid3D<Cell>(size, Vector3Int.zero);

        for (int x = 0; x < size.x; x++) {
            for (int y = 0; y < size.y; y++) {
                for (int z = 0; z < size.z; z++) {
                    grid[x, y, z] = new Cell();
                }
            }
        }
    }

    private void Generate() {
        //Generates every floor
        for (int i = 0; i < floorsCount; i++) {
            GenerateFloor(i);
        }

        //Connects neighbor floors with vents
        for (int i = 0; i < floorsCount - 1; i++) {
            HashSet<Prim.Edge> ventEdges = ConnectFloors(i, i + 1);
            edgesTotal.UnionWith(ventEdges);

            PathfindHallways(true, ref ventEdges);
        }

        InstantiateHallways();

        //Instantly moves camera back to player
        StartCoroutine(reactivateCamera());
    }

    private IEnumerator reactivateCamera () {
        yield return 0;
        cam.ForceCameraPosition (player.GetComponent<Rigidbody> ().position, cam.transform.rotation);
    }

    private Vector3Int GetRoomSize(int prefabIndex) {
        return roomPrefabs[prefabIndex].GetComponent<roomManager>().size;
    }


    private void GenerateFloor (int level) {

        List<Room> floorRooms;
        floorRooms = PlaceFloorRooms (level);

        Delaunay3D delaunay = Triangulate (ref floorRooms);

        HashSet<Prim.Edge> floorEdges;
        floorEdges = CreateEdges(ref floorRooms, ref delaunay);

        PathfindHallways(false, ref floorEdges);

        if (floorRooms != null && floorRooms.Count != 0) {
            roomsTotal.Add (floorRooms);
        }
        if (floorEdges != null && floorEdges.Count != 0) {
            edgesTotal.UnionWith(floorEdges);
        }
    }

    private List<Room> PlaceFloorRooms(int level) {
        List<Room> floorRooms = new List<Room>();

        PlaceLiftRoom (level, ref floorRooms);
        if (level == 0)
            PlaceStartRoom (ref floorRooms);

        for (int i = 0; i < roomCount; i++) {
            int roomIndex = random.Next(reservedIndex, roomPrefabs.Length);

            Vector3Int location = new Vector3Int(
                random.Next(0, size.x),
                level * levelHeight,
                random.Next(0, size.z)
            );

            Room newRoom = TryPlaceRoom(roomIndex, location, ref floorRooms);

            if (newRoom != null) {
                PlaceRoom(newRoom, ref floorRooms);
            }
        }

        return floorRooms;
    }

    private Room TryPlaceRoom(int roomIndex, Vector3Int location, ref List<Room> floorRooms) {
        Vector3Int roomSize = GetRoomSize(roomIndex);

        bool add = true;
        Room newRoom = new Room(location, roomSize, roomIndex);
        Room buffer = new Room(location + new Vector3Int(-hallWidth, 0, -hallWidth), 
                               roomSize + new Vector3Int(2 * hallWidth, 0, 2 * hallWidth), roomIndex);

        if (newRoom.bounds.xMin < 0 || newRoom.bounds.xMax >= size.x
            || newRoom.bounds.yMin < 0 || newRoom.bounds.yMax >= size.y
            || newRoom.bounds.zMin < 0 || newRoom.bounds.zMax >= size.z) {
            add = false;
            return null;
        }

        foreach (var room in floorRooms) {
            if (Room.Intersect(room, buffer)) {
                add = false;
                break;
            }
        }

        if (!add) {
            return null;
        }
        return newRoom;
    }

    private void PlaceStartRoom (ref List<Room> floorRooms) {
        int roomIndex = 1;
        int limit = 100000;

        while (limit > 0) {
            limit--;
            Vector3Int location = new Vector3Int (
                random.Next (0, size.x),
                0,
                random.Next (0, size.z)
            );

            Room newRoom = TryPlaceRoom(roomIndex, location, ref floorRooms);

            if (newRoom != null) {
                PlaceRoom(newRoom, ref floorRooms);
                player.GetComponent<Rigidbody>().position = newRoom.bounds.center;
                return;
            }
        }
        Debug.LogError ("unable to place start room");
    }

    private void PlaceLiftRoom(int level, ref List<Room> floorRooms) {
        Vector3Int liftSize = GetRoomSize(0);
        Room liftRoom;

        if (level % 2 == 0) {
            liftRoom = new Room(new Vector3Int(0, level * levelHeight, 0), liftSize, 0);
        }
        else {
            liftRoom = new Room(new Vector3Int(grid.Size.x - liftSize.x, level * levelHeight, grid.Size.z - liftSize.z), liftSize, 0);
        }

        PlaceRoom(liftRoom, ref floorRooms);
    }

    private void PlaceRoom (Room newRoom, ref List<Room> floorRooms) {
        floorRooms.Add (newRoom);

        foreach (var pos in newRoom.bounds.allPositionsWithin) {
            grid[pos].celltype = CellType.Room;
        }

        InstantiateRoom (newRoom.index, newRoom.bounds.position);
    }



    private Delaunay3D Triangulate(ref List<Room> floorRooms) {
        if (floorRooms.Count == 0) {
            Debug.LogWarning ("no rooms to triangulate");
            return null;
        }

        List<Vertex> vertices = new List<Vertex>();

        foreach (var room in floorRooms) {
            Vertex<Room> vertex = new Vertex<Room>(room.bounds.center, room);
            vertices.Add(vertex);
        }

        return Delaunay3D.Triangulate(vertices);
    }



    private HashSet<Prim.Edge> CreateEdges(ref List<Room> floorRooms, ref Delaunay3D delaunay) {
        if (floorRooms.Count == 0) {
            Debug.LogWarning ("No rooms found to generate hallways");
            return new HashSet<Prim.Edge>();
        }

        List<Prim.Edge> allEdges = new List<Prim.Edge>();

        foreach (var edge in delaunay.Edges) {
            allEdges.Add (new Prim.Edge (edge.U, edge.V));
        }

        List<Prim.Edge> minimumSpanningTree = Prim.MinimumSpanningTree(allEdges, allEdges[0].U);

        HashSet<Prim.Edge> floorEdges = new HashSet<Prim.Edge>(minimumSpanningTree);

        var remainingEdges = new HashSet<Prim.Edge>(allEdges);
        remainingEdges.ExceptWith(floorEdges);

        foreach (var edge in remainingEdges) {
            if (random.NextDouble() < 0.125) {
                floorEdges.Add(edge);
            }
        }

        return floorEdges;
        //foreach (var edge in selectedEdges) {
        //    Debug.DrawLine (edge.U.Position, edge.V.Position, Color.red, 99999);
        //}
    }

    private HashSet<Vector3Int> convertPathToFullPath(ref List<Vector3Int> path, bool ventMode) {
        HashSet<Vector3Int> fullWidthPath = new HashSet<Vector3Int>();
        for (int i = 0; i < path.Count; i++) {
            var current = path[i];

            foreach (var offset in (ventMode ? ventNeighbors : hallNeighbors)) {
                Vector3Int pos = current + offset;
                if (grid.InBounds(pos)) {
                    if (grid[pos].celltype == CellType.None) {
                        grid[pos].celltype = CellType.Hallway;
                        if ((i + 1 < path.Count && (path[i + 1] - current).y != 0) ||
                            (i - 1 >= 0 && (path[i - 1] - current).y != 0)) {
                            grid[pos].celltype = CellType.Vent;
                        }
                    }
                    fullWidthPath.Add(pos);
                }
            }
        }

        return fullWidthPath;
    }

    private void PathfindHallways(bool ventMode, ref HashSet<Prim.Edge> floorEdges) {
        if (floorEdges.Count == 0) {
            Debug.LogWarning ("No edges for hallway pathfinding");
            return;
        }

        DungeonPathfinder3D aStar = new DungeonPathfinder3D (size);

        foreach (var edge in floorEdges) {
            var startRoom = (edge.U as Vertex<Room>).Item;
            var endRoom = (edge.V as Vertex<Room>).Item;

            var startPosf = startRoom.bounds.center;
            var endPosf = endRoom.bounds.center;

            var startPos = new Vector3Int((int)startPosf.x, startRoom.bounds.position.y, (int)startPosf.z);
            var endPos = new Vector3Int((int)endPosf.x, endRoom.bounds.position.y, (int)endPosf.z);

            var path = aStar.FindPath(hallWidth, ventMode, 100000, startPos, endPos, (int pathW, DungeonPathfinder3D.Node a, DungeonPathfinder3D.Node b) => {
                var pathCost = new DungeonPathfinder3D.PathCost();
                var delta = b.Position - a.Position;

                if (!grid.InBounds (b.Position))
                    return pathCost;

                if (delta.y == 0) {
                    //flat hallway

                    pathCost.cost = Vector3Int.Distance(b.Position, endPos);    //heuristic

                    float c = hallWidthCost (b.Position, ventMode);
                    if (c == -1 || grid[b.Position].celltype == CellType.Vent)
                        return pathCost;

                    pathCost.cost += c;

                    if (grid[b.Position].celltype == CellType.Room) {
                        pathCost.cost += 30;
                    } else if (grid[b.Position].celltype == CellType.None) {
                        pathCost.cost += 1;
                    }

                    pathCost.traversable = true;
                } else if (ventMode) {

                    if (grid[b.Position].celltype != CellType.None)
                        return pathCost;

                    pathCost.cost = Vector3Int.Distance (b.Position, endPos);    //heuristic
                    float c = ventWidthCost (b.Position);
                    if (c == -1)
                        return pathCost;

                    pathCost.traversable = true;
                    pathCost.isVent = true;
                }

                return pathCost;
            });

            if (path != null) {
                HashSet<Vector3Int> fullWidthPath = convertPathToFullPath(ref path, ventMode);

                foreach (var pos in fullWidthPath) {
                    if (grid.InBounds(pos)) {
                        if (grid[pos].celltype == CellType.Hallway ||
                            grid[pos].celltype == CellType.Vent) {
                            grid[pos].hallConfig &= generateHallConfig(ref fullWidthPath, pos);
                        }
                    }
                }
            } else {
                Debug.LogWarning ("Path " + startPos.ToString() + " - " + endPos.ToString() + " not found");
            }
        }
    }

    void InstantiateHallways() {
        Queue<Vector3Int> q = new Queue<Vector3Int>();

        foreach (var edge in edgesTotal) {
            var startRoom = (edge.U as Vertex<Room>).Item;
            var startPosf = startRoom.bounds.center;

            var startPos = new Vector3Int((int)startPosf.x, startRoom.bounds.position.y, (int)startPosf.z);

            q.Enqueue(startPos);
        }

        while(q.Count > 0) {
            Vector3Int start = q.Dequeue();

        }
    }

    private HashSet<Prim.Edge> ConnectFloors(int a, int b) {
        HashSet<Prim.Edge> ventEdges = new HashSet<Prim.Edge> ();

        for (int i = 0; i < roomsTotal[a].Count; i++) {
            int maxStairs = 0;
            for (int j = 0; j <= 4; j++) {
                if (random.NextDouble () < stairsProbability / (float)(1 << j))
                    maxStairs++;
                else
                    break;
            }
            maxStairs = Mathf.Min(maxStairs, roomsTotal[b].Count);

            roomsTotal[b].Sort((room1, room2) => Vector3.Distance(room1.bounds.position, roomsTotal[a][i].bounds.position).CompareTo(
                                                 Vector3.Distance(room2.bounds.position, roomsTotal[a][i].bounds.position)));

            for (int j = 0; j < maxStairs; j++) {
                ventEdges.Add(new Prim.Edge(new Vertex<Room>(roomsTotal[a][i].bounds.center, roomsTotal[a][i]),
                                            new Vertex<Room> (roomsTotal[b][j].bounds.center, roomsTotal[b][j]))
                                                );
                //Debug.Log ("Floor #" + a.ToString () + " Room #" + roomsTotal[a][i].bounds.position.ToString() + " Floor #" + b.ToString () + " Room #" + roomsTotal[b][j].bounds.position.ToString ());
            }
        }
        return ventEdges;
    }

    int generateHallConfig (ref HashSet<Vector3Int> allHalls, Vector3Int pos) {
        int result = 0;
        for (int i = 0; i < Cell.neighbors.Length; i++) {
            Vector3Int location = pos + Cell.neighbors[i];
            if (allHalls.Contains (location)) {
                continue;
            }
            result = (result | (1 << i));
            if (pos == new Vector3Int(0, 2, 3)) {
                Debug.Log ("wall exists " + location + " - " + grid[location].celltype + " new bitmask = " + result);
            }
        }
        return result;
    }
    Vector3Int[] getHallNeighborsList (int hallWidth) {
        Vector3Int[] hallNeighbors = new Vector3Int[hallWidth * hallWidth * hallWidth];
        int cnt = 0;
        for (int i = -hallWidth / 2; i <= (hallWidth / 2) - (1 - (hallWidth % 2)); i++) {
            for (int j = -hallWidth / 2; j <= (hallWidth / 2) - (1 - (hallWidth % 2)); j++) {
                for (int k = 0; k < hallWidth; k++) {
                    hallNeighbors[cnt] = new Vector3Int (i, k, j);
                    cnt++;
                }
            }
        }
        return hallNeighbors;
    }
    int hallWidthCost (Vector3Int nodePos, bool ventMode) {
        int cost = 0;
        foreach (var offset in (ventMode ? ventNeighbors : hallNeighbors)){ 
            Vector3Int pos = nodePos + offset;
            if (offset == Vector3Int.zero)
                continue;
            if (!grid.InBounds (pos) || grid[pos].celltype == CellType.Vent) {
                return -1;
            }
            if (grid[pos].celltype == CellType.Room) {
                cost += 30;
            }
            if (ventMode && grid[pos].celltype == CellType.Hallway)
                cost += 2;
        }

        return cost;
    }
    int ventWidthCost (Vector3Int nodePos) {
        int cost = 0;
        foreach (var offset in ventNeighbors) {
            Vector3Int pos = nodePos + offset;
            if (offset == Vector3Int.zero)
                continue;
            if (!grid.InBounds (pos) || grid[pos].celltype == CellType.Vent) {
                return -1;
            }
            if (grid[pos].celltype != CellType.None) {
                return -1;
            }
        }

        return cost;
    }

    void InstantiateRoom(int roomIndex, Vector3Int location) {
        GameObject go = Instantiate (roomPrefabs[roomIndex], location, Quaternion.identity, transform);
        SetGameLayerRecursive (go, LayerMask.NameToLayer ("floor" + (location.y / levelHeight)));
    }

    void PlaceHallway(Vector3Int location) {
        grid[location].generateWalls (location, wallPrefab, levelHeight);
//        PlaceCube(location, new Vector3Int(1, 1, 1), blueMaterial);
    }

    void PlaceVent(Vector3Int location) {
        grid[location].generateWalls (location, wallPrefab, levelHeight);
//        PlaceCube (location, new Vector3Int(1, 1, 1), greenMaterial);
    }

    private static void SetGameLayerRecursive (GameObject _go, int _layer) {
        _go.layer = _layer;
        foreach (Transform child in _go.transform) {
            child.gameObject.layer = _layer;

            Transform _HasChildren = child.GetComponentInChildren<Transform> ();
            if (_HasChildren != null)
                SetGameLayerRecursive (child.gameObject, _layer);

        }
    }
}
