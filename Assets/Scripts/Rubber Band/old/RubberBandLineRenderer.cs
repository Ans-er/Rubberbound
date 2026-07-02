using UnityEngine;

namespace RubberBand
{
    /// <summary>
    /// v0.1 renderer: reads particle positions from the band actor, feeds a LineRenderer.
    /// Will be replaced by mesh extrusion + path smoothing in a later version.
    /// 
    /// Mirrors Obi's separation of physics (ObiRope) from visuals (ObiRopeExtrudedRenderer).
    /// Runs in LateUpdate so it reads positions after FixedUpdate has finished.
    /// </summary>
    [RequireComponent(typeof(RubberBand))]
    [RequireComponent(typeof(LineRenderer))]
    public class RubberBandLineRenderer : MonoBehaviour
    {
        private RubberBand _band;
        private LineRenderer _lineRenderer;
        private Vector3[] _positionBuffer;

        private void Awake()
        {
            _band = GetComponent<RubberBand>();
            _lineRenderer = GetComponent<LineRenderer>();
        }

        private void LateUpdate()
        {
            if (!_band.isInitialized) return;

            // Lazy-init buffer
            if (_positionBuffer == null || _positionBuffer.Length != _band.particleCount)
            {
                _positionBuffer = new Vector3[_band.particleCount];
                _lineRenderer.positionCount = _band.particleCount;
            }

            _band.GetParticlePositions(_positionBuffer);
            _lineRenderer.SetPositions(_positionBuffer);
        }
    }
}
