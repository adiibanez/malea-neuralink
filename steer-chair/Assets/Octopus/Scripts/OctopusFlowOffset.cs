using UnityEngine;

namespace Warcool.OctopusUnderwater
{
    public class OctopusFlowOffset : MonoBehaviour
    {
        [Header("Material and texture settings")]
        public Renderer targetRenderer;         // Reference to the Renderer object
        public string texturePropertyName = "_MainTex"; // Texture parameter name (usually _MainTex)

        [Header("Flow parameters")]
        public float flowSpeedX = 0.1f; // Texture displacement speed along the X axis
        public float flowSpeedY = 0.0f; // Texture displacement speed along the Y axis

        private Material targetMaterial;
        private Vector2 currentOffset;

        private void Start()
        {
            // We get a copy of the material so as not to change the material at the project level
            targetMaterial = targetRenderer.material;
            currentOffset = targetMaterial.GetTextureOffset(texturePropertyName);
        }

        private void Update()
        {
            // Changing offset depending on time
            float deltaX = flowSpeedX * Time.deltaTime;
            float deltaY = flowSpeedY * Time.deltaTime;

            currentOffset.x += deltaX;
            currentOffset.y += deltaY;

            // Applying an offset to a material
            targetMaterial.SetTextureOffset(texturePropertyName, currentOffset);
        }
    }
}


