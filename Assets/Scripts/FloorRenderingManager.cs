using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FloorRenderingManager : MonoBehaviour
{
    [SerializeField] Transform player;
    [SerializeField] Camera cam;
    [SerializeField] Generator3D gen;

    int lastLevel = 0;

    private void Update () {
        GameObject sceneCamObj = GameObject.Find ("SceneCamera");
        if (sceneCamObj != null) {
            // Should output the real dimensions of scene viewport
//            Debug.Log (Screen.width + " " + Screen.height);
        }
        int newLevel = Mathf.RoundToInt(player.position.y) / gen.levelHeight;
        if (lastLevel != newLevel) {
            for (int i = 0; i <= lastLevel; i++)
                ClearLevelLayer (i);
            SetLevelLayer (newLevel);
            SetLevelLayer (newLevel - 1);
            lastLevel = newLevel;
        }
    }
    private void SetLevelLayer(int level) {
        if (level < 0)
            return;
        int layer = LayerMask.NameToLayer ("floor" + level);
        cam.cullingMask |= (1<<layer);
    }
    private void ClearLevelLayer (int level) {
        int layer = LayerMask.NameToLayer ("floor" + level);
        cam.cullingMask &= ~(1<<layer);
    }
}
