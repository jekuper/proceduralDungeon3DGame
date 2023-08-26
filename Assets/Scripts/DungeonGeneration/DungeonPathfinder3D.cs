using System;
using System.Collections.Generic;
using UnityEngine;
using BlueRaja;
using UnityEditor;

public class DungeonPathfinder3D {
    //similar to A*, with some modifications specifically for stair cases
    public class Node {
        public Vector3Int Position { get; private set; }
        public Node Previous { get; set; }
        public float Cost { get; set; }

        public Node(Vector3Int position) {
            Position = position;
        }
    }

    public struct PathCost {
        public bool traversable;
        public float cost;
        public bool isVent;
    }

    static readonly Vector3Int[] neighbors = {
        new Vector3Int(1, 0, 0),
        new Vector3Int(-1, 0, 0),
        new Vector3Int(0, 0, 1),
        new Vector3Int(0, 0, -1),

        new Vector3Int(0, 1, 0),
        new Vector3Int(0, -1, 0),
    };

    public Grid3D<Node> grid;
    SimplePriorityQueue<Node, float> queue;
    HashSet<Node> closed;
    Stack<Vector3Int> stack;

    public DungeonPathfinder3D(Vector3Int size) {
        grid = new Grid3D<Node>(size, Vector3Int.zero);

        queue = new SimplePriorityQueue<Node, float>();
        closed = new HashSet<Node>();
        stack = new Stack<Vector3Int>();

        for (int x = 0; x < size.x; x++) {
            for (int y = 0; y < size.y; y++) {
                for (int z = 0; z < size.z; z++) {
                    grid[x, y, z] = new Node(new Vector3Int(x, y, z));
                }
            }
        }
    }

    void ResetNodes() {
        var size = grid.Size;

        for (int x = 0; x < size.x; x++) {
            for (int y = 0; y < size.y; y++) {
                for (int z = 0; z < size.z; z++) {
                    var node = grid[x, y, z];
                    node.Previous = null;
                    node.Cost = float.PositiveInfinity;
                }
            }
        }
    }

    public List<Vector3Int> FindPath(int pathW, bool ventMode, int cellLimit, Vector3Int start, Vector3Int end, Func<int, Node, Node, PathCost> costFunction) {
        ResetNodes();
        queue.Clear();
        closed.Clear();

        queue = new SimplePriorityQueue<Node, float>();
        closed = new HashSet<Node>();

        grid[start].Cost = 0;
        queue.Enqueue(grid[start], 0);

        while (queue.Count > 0) {
            if (cellLimit <= 0) {
                return null;
            }
            cellLimit--;
            Node node = queue.Dequeue();
            closed.Add(node);

            if (node.Position == end) {
                return ReconstructPath(node);
            }

            foreach (var offset in neighbors) {
                if (!ventMode && offset.y != 0)
                    continue;
                if (!grid.InBounds(node.Position + offset))
                    continue;
                var neighbor = grid[node.Position + offset];
                if (closed.Contains(neighbor)) continue;
                if (ventMode && neighbor.Position.y != start.y && neighbor.Position.y != end.y && (offset.x != 0 || offset.z != 0))
                    continue;

                var pathCost = costFunction(pathW, node, neighbor);
                if (!pathCost.traversable) continue;


                float newCost = node.Cost + pathCost.cost;

                if (newCost < neighbor.Cost) {
                    neighbor.Previous = node;
                    neighbor.Cost = newCost;

                    if (queue.TryGetPriority(node, out float existingPriority)) {
                        queue.UpdatePriority(node, newCost);
                    } else {
                        queue.Enqueue(neighbor, neighbor.Cost);
                    }
                }
            }
        }

        return null;
    }

    List<Vector3Int> ReconstructPath(Node node) {
        List<Vector3Int> result = new List<Vector3Int>();

        while (node != null) {
            stack.Push(node.Position);
            node = node.Previous;
        }

        while (stack.Count > 0) {
            result.Add(stack.Pop());
        }

        return result;
    }
}
