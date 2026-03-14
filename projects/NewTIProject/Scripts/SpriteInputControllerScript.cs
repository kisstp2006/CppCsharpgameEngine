using Engine;

namespace GameScripts
{
    public sealed class SpriteInputControllerScript : MonoBehaviour
    {
        private float _speed = 260.0f;

        protected override void Start()
        {
            Debug.Log("[Starter] WASD movement is active for '" + gameObject.name + "'.");
        }

        protected override void Update()
        {
            Vector3 move = new Vector3(0.0f, 0.0f, 0.0f);
            if (Input.GetKey(KeyCode.A))
                move.x -= 1.0f;
            if (Input.GetKey(KeyCode.D))
                move.x += 1.0f;
            if (Input.GetKey(KeyCode.S))
                move.y -= 1.0f;
            if (Input.GetKey(KeyCode.W))
                move.y += 1.0f;

            if (move.x == 0.0f && move.y == 0.0f)
                return;

            Vector3 position = transform.position;
            position.x += move.x * _speed * Time.deltaTime;
            position.y += move.y * _speed * Time.deltaTime;
            transform.position = position;
        }
    }
}
