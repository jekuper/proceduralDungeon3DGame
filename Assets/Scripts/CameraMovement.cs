using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraMovement : MonoBehaviour
{
    public Transform target;
    [SerializeField] CinemachineVirtualCamera cinemachineOffset;
    public Vector3 offset = new Vector3(0f, 15f, 0f);
    public Vector3 maxCursorOffset = new Vector3 (4f, 0f, 4f);

    void Update()
    {
        Vector3 cursorOffset = GetCursorOffset ();
        cinemachineOffset.GetCinemachineComponent<CinemachineFramingTransposer>().m_TrackedObjectOffset = cursorOffset;
    }
    Vector3 GetCursorOffset () {
        if (Input.GetMouseButton (1) || !Input.GetMouseButton(0)) {
            return Vector3.zero;
        }
        Vector2 cursorPos = Input.mousePosition;
        Vector2 direction = Vector2.zero;

        if (cursorPos.x < 0 || cursorPos.x > Screen.width || cursorPos.y < 0 || cursorPos.y > Screen.height) {
            return Vector3.zero;
        }

        if (cursorPos.x < Screen.width / 3f) {
            direction.x = -1;
        }
        if (cursorPos.x > Screen.width / 3f * 2) {
            direction.x = 1;
        }

        if (cursorPos.y < Screen.height / 3f) {
            direction.y = -1;
        }
        if (cursorPos.y > Screen.height / 3f * 2) {
            direction.y = 1;
        }


        Vector3 camForward = cinemachineOffset.transform.forward * direction.y;
        Vector3 camRight = cinemachineOffset.transform.right * direction.x;
        camForward.y = 0;
        camRight.y = 0;
        camForward.Normalize ();
        camRight.Normalize ();

        Vector3 resWorld = camForward + camRight;
        resWorld = Vector3.Scale (resWorld, maxCursorOffset);

        return target.InverseTransformDirection (resWorld);
    }
}
