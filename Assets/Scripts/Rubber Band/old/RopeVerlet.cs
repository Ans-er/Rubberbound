using CMF;
using System.Collections.Generic;
using UnityEngine;

public class RopeVerlet : MonoBehaviour
{
    [Header("Rope")]
    [SerializeField] private int _numOfRopeSegments = 50;
    [SerializeField] private float _ropeSegmentLength = 0.225f;

    [Header("Physics")]
    [SerializeField] private Vector3 _gravityForce = new Vector3(0f, -2f, 0f);
    [SerializeField] private float _dampingFactor = 0.98f;
    [SerializeField] private LayerMask _collisionMask;
    [SerializeField] private float _collisionRadius = 0.1f;
    [SerializeField] private float _bounceFactor = 0.5f;
    [SerializeField] private float _correctionClampAmount = 0.1f;

    [Header("Constraints")]
    [SerializeField] private int _numOfConstraintRuns = 50;

    [Header("Optimizations")]
    [SerializeField] private int _collisionSegmentInterval = 1;

    private LineRenderer _lineRenderer;
    private List<RopeSegment> _ropeSegments = new List<RopeSegment>();

    private Transform _playerA;
    private Transform _playerB;

    private float _attachHeightA;
    private float _attachHeightB;

    private bool _initialized = false;

    private void Awake()
    {
        
    }

    private void Update()
    {
        if (!_initialized)
        {
            TryFindPlayers();
            return;
        }

        DrawRope();
    }

    private void FixedUpdate()
    {
        if (!_initialized) return;

        if(_playerA == null || _playerB == null)
        {
            return;
        }

        Simulate();

        for (int i = 0; i < _numOfConstraintRuns; i++)
        {
            ApplyConstraints();

            if (i % _collisionSegmentInterval == 0)
                HandleCollisions();
        }
    }

    private void TryFindPlayers()
    {
        // Find all spawned player objects in the scene
        var players = FindObjectsOfType<AdvancedWalkerController>(); // whatever component your player has

        if (players.Length >= 2)
        {
            AttachPlayers(players[0].transform, players[1].transform);
        }
    }

    public void AttachPlayers(Transform a, Transform b)
    {
        _playerA = a;
        _playerB = b;

        _attachHeightA = a.GetComponent<Collider>().bounds.size.y / 2f;
        _attachHeightB = b.GetComponent<Collider>().bounds.size.y / 2f;

        Vector3 startPos = _playerA.position + Vector3.up * _attachHeightA;
        Vector3 endPos = _playerB.position + Vector3.up * _attachHeightB;

        _lineRenderer = GetComponent<LineRenderer>();
        _lineRenderer.positionCount = _numOfRopeSegments;

        for (int i = 0; i < _numOfRopeSegments; i++)
        {
            float t = (float)i / (_numOfRopeSegments - 1);
            Vector3 pos = Vector3.Lerp(startPos, endPos, t);
            _ropeSegments.Add(new RopeSegment(pos));
        }

        _initialized = true;
    }

    private void DrawRope()
    {
        Vector3[] ropePositions = new Vector3[_numOfRopeSegments];
        for (int i = 0; i < _ropeSegments.Count; i++)
        {
            ropePositions[i] = _ropeSegments[i].CurrentPosition;
        }

        _lineRenderer.SetPositions(ropePositions);
    }

    private void Simulate()
    {
        for (int i = 0; i < _ropeSegments.Count; i++)
        {
            RopeSegment segment = _ropeSegments[i];
            Vector3 velocity = (segment.CurrentPosition - segment.OldPosition) * _dampingFactor;

            segment.OldPosition = segment.CurrentPosition;
            segment.CurrentPosition += velocity;
            segment.CurrentPosition += _gravityForce * Time.fixedDeltaTime;
            _ropeSegments[i] = segment;
        }
    }

    private void ApplyConstraints()
    {
        RopeSegment firstSegment = _ropeSegments[0];
        firstSegment.CurrentPosition = _playerA.position + Vector3.up * _attachHeightA;
        _ropeSegments[0] = firstSegment;

        RopeSegment lastSegment = _ropeSegments[_ropeSegments.Count - 1];
        lastSegment.CurrentPosition = _playerB.position + Vector3.up * _attachHeightB;
        _ropeSegments[_ropeSegments.Count - 1] = lastSegment;

        for (int i = 0; i < _numOfRopeSegments - 1; i++)
        {
            RopeSegment currentSeg = _ropeSegments[i];
            RopeSegment nextSeg = _ropeSegments[i + 1];

            float dist = (currentSeg.CurrentPosition - nextSeg.CurrentPosition).magnitude;
            float difference = (dist - _ropeSegmentLength);

            Vector3 changeDir = (currentSeg.CurrentPosition - nextSeg.CurrentPosition).normalized;
            Vector3 changeVector = changeDir * difference;

            bool isFirst = (i == 0);
            bool isNextLast = (i + 1 == _ropeSegments.Count - 1);

            if (isFirst)
            {
                nextSeg.CurrentPosition += changeVector;
            }
            else if (isNextLast)
            {
                currentSeg.CurrentPosition -= changeVector;
            }
            else
            {
                currentSeg.CurrentPosition -= changeVector * 0.5f;
                nextSeg.CurrentPosition += changeVector * 0.5f;
            }

            _ropeSegments[i] = currentSeg;
            _ropeSegments[i + 1] = nextSeg;
        }
    }

    private void HandleCollisions()
    {
        for (int i = 1; i < _ropeSegments.Count - 1; i++)
        {
            RopeSegment segment = _ropeSegments[i];
            Vector3 velocity = segment.CurrentPosition - segment.OldPosition;

            Collider[] colliders = Physics.OverlapSphere(segment.CurrentPosition, _collisionRadius, _collisionMask);

            foreach (Collider collider in colliders)
            {
                Vector3 closestPoint = collider.ClosestPoint(segment.CurrentPosition);
                float distance = Vector3.Distance(segment.CurrentPosition, closestPoint);

                if (distance < _collisionRadius)
                {
                    Vector3 normal = (segment.CurrentPosition - closestPoint).normalized;
                    if (normal == Vector3.zero)
                    {
                        normal = (segment.CurrentPosition - collider.transform.position).normalized;
                    }

                    float depth = _collisionRadius - distance;
                    segment.CurrentPosition += normal * depth;

                    velocity = Vector3.Reflect(velocity, normal) * _bounceFactor;
                }
            }

            segment.OldPosition = segment.CurrentPosition - velocity;
            _ropeSegments[i] = segment;
        }
    }

    public struct RopeSegment
    {
        public Vector3 CurrentPosition;
        public Vector3 OldPosition;

        public RopeSegment(Vector3 pos)
        {
            CurrentPosition = pos;
            OldPosition = pos;
        }
    }
}
