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

    Random random;
    Grid3D<Cell> grid;
    List<List<Room>> roomsTotal;
    List<Room> rooms;
    Delaunay3D delaunay;
    HashSet<Prim.Edge> selectedEdges;

    Vector3Int[] hallNeighbors {
        get { return getHallNeighborsList (hallWidth); }
    }
    Vector3Int[] ventNeighbors {
        get { return getHallNeighborsList (ventWidth); }
    }

    void Start() {
        random = new Random(0);
        grid = new Grid3D<Cell> (size, Vector3Int.zero);
        roomsTotal = new List<List<Room>> ();
        for (int x = 0; x < size.x; x++) {
            for (int y = 0; y < size.y; y++) {
                for (int z = 0; z < size.z; z++) {
                    grid[x, y, z] = new Cell ();
                }
            }
        }
        Debug.Log (grid[0, 2, 3].hallConfig);

        for (int i = 0; i < floorsCount; i++) {
            Generate (i);
        }
        for (int i = 0; i < floorsCount - 1; i++) {
            ConnectFloors (i, i + 1);
            PathfindHallways (true);
        }
        StartCoroutine (reactivateCamera ());
        Debug.Log (grid[0, 2, 3].hallConfig);
    }
    IEnumerator reactivateCamera () {
        yield return 0;
        cam.ForceCameraPosition (player.GetComponent<Rigidbody> ().position, cam.transform.rotation);
    }

    void Generate (int level) {
        rooms = new List<Room> ();

        PlaceRooms (level);
        Triangulate ();
        CreateHallways(level);
        PathfindHallways(false);

        if (rooms != null && rooms.Count != 0)
            roomsTotal.Add (rooms);
    }

    void PlaceRooms(int level) {

        PlaceLiftRoom (level);
        if (level == 0)
            PlaceStartRoom ();

        for (int i = 0; i < roomCount; i++) {
            int roomIndex = random.Next(reservedIndex, roomPrefabs.Length);

            Vector3Int location = new Vector3Int(
                random.Next(0, size.x),
                level * levelHeight,
                random.Next(0, size.z)
            );

            Vector3Int roomSize = roomPrefabs[roomIndex].GetComponent<roomManager> ().size;

            bool add = true;
            Room newRoom = new Room(location, roomSize, roomIndex);
            Room buffer = new Room(location + new Vector3Int(-hallWidth, 0, -hallWidth), roomSize + new Vector3Int(2 * hallWidth, 0, 2 * hallWidth), roomIndex);

            foreach (var room in rooms) {
                if (Room.Intersect(room, buffer)) {
                    add = false;
                    break;
                }
            }

            if (newRoom.bounds.xMin < 0 || newRoom.bounds.xMax >= size.x
                || newRoom.bounds.yMin < 0 || newRoom.bounds.yMax >= size.y
                || newRoom.bounds.zMin < 0 || newRoom.bounds.zMax >= size.z) {
                add = false;
            }

            if (add) {
                PlaceRoom (newRoom);
            }
        }
    }

    void PlaceStartRoom () {
        Vector3Int liftSize = roomPrefabs[1].GetComponent<roomManager> ().size;
        int roomIndex = 1;
        int limit = 100000;

        while (limit > 0) {
            limit--;
            Vector3Int location = new Vector3Int (
                random.Next (0, size.x),
                0,
                random.Next (0, size.z)
            );
            
            Vector3Int roomSize = roomPrefabs[roomIndex].GetComponent<roomManager> ().size;

            bool add = true;
            Room newRoom = new Room(location, roomSize, roomIndex);
            Room buffer = new Room(location + new Vector3Int(-hallWidth, 0, -hallWidth), roomSize + new Vector3Int(2 * hallWidth, 0, 2 * hallWidth), roomIndex);

            foreach (var room in rooms) {
                if (Room.Intersect(room, buffer)) {
                    add = false;
                    break;
                }
            }

            if (newRoom.bounds.xMin < 0 || newRoom.bounds.xMax >= size.x
                || newRoom.bounds.yMin < 0 || newRoom.bounds.yMax >= size.y
                || newRoom.bounds.zMin < 0 || newRoom.bounds.zMax >= size.z) {
                add = false;
            }

            if (add) {
                PlaceRoom (newRoom);
                player.GetComponent<Rigidbody>().position = newRoom.bounds.center;                
                return;
            }
        }
        Debug.LogError ("unable to place start room");
    }
    void PlaceLiftRoom(int level) {
        Vector3Int liftSize = roomPrefabs[0].GetComponent<roomManager> ().size;
        if (level % 2 == 0)
            PlaceRoom (new Room (new Vector3Int (0, level * levelHeight, 0), liftSize, 0));
        else
            PlaceRoom (new Room (new Vector3Int (grid.Size.x - liftSize.x, level * levelHeight, grid.Size.z - liftSize.z), liftSize, 0));
    }

    void PlaceRoom (Room newRoom) {
        rooms.Add (newRoom);
        InstantiateRoom (newRoom.index, newRoom.bounds.position);

        foreach (var pos in newRoom.bounds.allPositionsWithin) {
            grid[pos].celltype = CellType.Room;
        }
    }

    void Triangulate() {
        if (rooms.Count == 0) {
            Debug.Log ("no rooms to triangulate");
            return;
        }

        List<Vertex> vertices = new List<Vertex>();

        foreach (var room in rooms) {
            vertices.Add(new Vertex<Room>((Vector3)room.bounds.position + ((Vector3)room.bounds.size) / 2, room));
        }

        delaunay = Delaunay3D.Triangulate(vertices);
    }

    void CreateHallways(int level) {
        if (rooms.Count == 0) {
            Debug.Log ("no rooms generated");
            return;
        }

        List<Prim.Edge> edges = new List<Prim.Edge>();

        foreach (var edge in delaunay.Edges) {
            edges.Add (new Prim.Edge (edge.U, edge.V));
        }

        List<Prim.Edge> minimumSpanningTree = Prim.MinimumSpanningTree(edges, edges[0].U);

        selectedEdges = new HashSet<Prim.Edge>(minimumSpanningTree);
        var remainingEdges = new HashSet<Prim.Edge>(edges);
        remainingEdges.ExceptWith(selectedEdges);

        foreach (var edge in remainingEdges) {
            if (random.NextDouble() < 0.125) {
                selectedEdges.Add(edge);
            }
        }
        //foreach (var edge in selectedEdges) {
        //    Debug.DrawLine (edge.U.Position, edge.V.Position, Color.red, 99999);
        //}
    }

    void PathfindHallways(bool ventMode) {
        if (rooms.Count == 0) {
            Debug.Log ("no rooms for pathfind");
            return;
        }

        DungeonPathfinder3D aStar = new DungeonPathfinder3D (size);

        foreach (var edge in selectedEdges) {
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
                HashSet<Vector3Int> allPath = new HashSet<Vector3Int> ();
                for (int i = 0; i < path.Count; i++) {
                    var current = path[i];

                    foreach(var offset in (ventMode ? ventNeighbors : hallNeighbors)) {
                        Vector3Int pos = current + offset;
                        if (grid.InBounds(pos) && grid[pos].celltype == CellType.None) {
                            grid[pos].celltype = CellType.Hallway;
                            if ((i + 1 < path.Count && (path[i + 1] - current).y != 0) ||
                                (i - 1 >= 0 && (path[i - 1] - current).y != 0)) {
                                grid[pos].celltype = CellType.Vent;
                            }
                        }
                        if (grid.InBounds(pos) && (grid[pos].celltype != CellType.None))
                            allPath.Add (pos);
                    }
                }


                foreach (var pos in path) {
                    foreach (var offset in (ventMode ? ventNeighbors : hallNeighbors)) {
                        Vector3Int neighbor = pos + offset;
                        if (grid.InBounds(neighbor)) {
                            if (grid[neighbor].celltype == CellType.Hallway) {
                                if (neighbor == new Vector3Int(0, 2, 3))
                                    Debug.Log ("starting from " + pos);
                                grid[neighbor].hallConfig &= generateHallConfig (ref allPath, neighbor);
                                PlaceHallway (neighbor);
                            }
                            else if (grid[neighbor].celltype == CellType.Vent) {
                                grid[neighbor].hallConfig &= generateHallConfig (ref allPath, neighbor);
                                PlaceVent (neighbor);
                            }
                        }
                    }
                }
            } else {
                Debug.LogWarning ("Path " + startPos.ToString() + " - " + endPos.ToString() + " not found");
            }
        }
    }

    void ConnectFloors(int a, int b) {
        selectedEdges = new HashSet<Prim.Edge> ();
        for (int i = 0; i < roomsTotal[a].Count; i++) {
            int maxStairs = 0;
            for (int j = 0; j <= 4; j++) {
                if (random.NextDouble () < stairsProbability / (float)(1 << j))
                    maxStairs++;
                else
                    break;
            }

            roomsTotal[b].Sort ((room1, room2) => Vector3.Distance (room1.bounds.position, roomsTotal[a][i].bounds.position).CompareTo(
                                                  Vector3.Distance (room2.bounds.position, roomsTotal[a][i].bounds.position)));

            for (int j = 0; j < Mathf.Min(maxStairs, roomsTotal[b].Count); j++) {
                selectedEdges.Add(new Prim.Edge(new Vertex<Room>(roomsTotal[a][i].bounds.position, roomsTotal[a][i]),
                                                new Vertex<Room> (roomsTotal[b][j].bounds.position, roomsTotal[b][j]))
                                                );
                Debug.Log ("Floor #" + a.ToString () + " Room #" + roomsTotal[a][i].bounds.position.ToString() + " Floor #" + b.ToString () + " Room #" + roomsTotal[b][j].bounds.position.ToString ());
//                return;
            }
//            Debug.Log ("Floor #" + a.ToString() + " Room #" + i.ToString() + " stairs: " + maxStairs.ToString());
        }
        Debug.Log (selectedEdges.Count);
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
