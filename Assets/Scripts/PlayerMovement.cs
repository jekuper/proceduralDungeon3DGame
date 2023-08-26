using Cinemachine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float acceleration = 5f;
    public float maxVelocity = 8f;

    private Rigidbody rb;
    private Vector3 movement = Vector3.zero;
    [SerializeField] CinemachineVirtualCamera cam;


    private void Start () {
        rb = GetComponent<Rigidbody> ();
    }

    private void Update () {
        
        movement.x = 0;
        movement.z = 0;
        Vector3 camForward = cam.transform.forward;
        Vector3 camRight = cam.transform.right;
        camForward.y = 0;
        camRight.y = 0;
        camForward.Normalize ();
        camRight.Normalize ();

        camForward *= Input.GetAxisRaw ("Vertical");
        camRight *= Input.GetAxisRaw ("Horizontal");
        movement = camForward + camRight;
        if (Input.GetKey (KeyCode.Space))
            movement.y = 1;
        else
            movement.y = 0;
    }

    private void FixedUpdate () {
        //        direction.Normalize ();

//        transform.Translate (direction * speed * Time.fixedDeltaTime);
        rb.AddForce (movement * acceleration, ForceMode.Acceleration);
        rb.velocity = new Vector3 (Mathf.Abs (rb.velocity.x) * movement.x, rb.velocity.y, Mathf.Abs (rb.velocity.z) * movement.z);
        rb.velocity = Vector3.ClampMagnitude (rb.velocity, maxVelocity);
        
        //        rb.MovePosition (transform.position + (movement * speed * Time.fixedDeltaTime));

    }
}
