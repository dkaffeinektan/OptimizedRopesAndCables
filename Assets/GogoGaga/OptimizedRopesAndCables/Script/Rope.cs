using UnityEngine;
using System;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Authoring;

namespace GogoGaga.OptimizedRopesAndCables
{
    /// <summary>
    /// GameObject side of a rope. In play mode it owns an entity simulated by <see cref="RopeSimulationSystem"/>
    /// (Verlet particles colliding with Unity Physics colliders) and renders the particles it reads back.
    /// In edit mode it only shows the initial hanging shape, since no physics world runs there.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(LineRenderer))]
    public class Rope : MonoBehaviour
    {
        public event Action OnPointsChanged;

        [Header("Rope Transforms")]
        [Tooltip("The rope will start at this point")]
        [SerializeField] private Transform startPoint;
        public Transform StartPoint => startPoint;

        [Tooltip("This will move at the center hanging from the rope, like a necklace, for example")]
        [SerializeField] private Transform midPoint;
        public Transform MidPoint => midPoint;

        [Tooltip("The rope will end at this point")]
        [SerializeField] private Transform endPoint;
        public Transform EndPoint => endPoint;

        [Header("Rope Settings")]
        [Tooltip("How many segments the rope has, 2 would be a triangle with straight lines, 100 would be a very flexible rope with many parts")]
        [Range(2, 100)] public int linePoints = 10;

        [Tooltip("Rendered points per rope segment. The rope is drawn as a smooth curve through the simulated points, so higher values look rounder at no simulation cost")]
        [Range(1, 16)] public int smoothingSubdivisions = 4;

        [Tooltip("How many times per physics step segment lengths and collisions are solved. Higher values stretch less and pass through colliders less, at a higher cost")]
        [Range(1, 50)] public int constraintIterations = 20;

        [Tooltip("0 is no damping, 50 is a lot")]
        public float damping = 15f;

        [Tooltip("How long is the rope, it will hang more or less from starting point to end point depending on this value")]
        public float ropeLength = 15;

        [Tooltip("The Rope width. Half of it is the collision radius of the rope")]
        public float ropeWidth = 0.1f;

        [Header("Collision")]
        [Tooltip("Unity Physics categories the rope belongs to")]
        public PhysicsCategoryTags belongsTo = new PhysicsCategoryTags { Category01 = true };

        [Tooltip("Unity Physics categories of the colliders the rope collides with")]
        public PhysicsCategoryTags collidesWith = new PhysicsCategoryTags { Category00 = true };

        [Header("Midpoint Position")]
        [Tooltip("Position of the Mid Point transform along the rope")]
        [Range(0.25f, 0.75f)] public float midPointPosition = 0.5f;

        /// <summary>
        /// Velocity change applied to the rope every <see cref="Time.fixedDeltaTime"/> step, wind for example.
        /// </summary>
        public Vector3 otherPhysicsFactors { get; set; }

        private Vector3[] particlePositions = new Vector3[0];
        private Vector3[] renderedPositions = new Vector3[0];
        private LineRenderer lineRenderer;
        private Entity ropeEntity = Entity.Null;

        private Vector3 prevStartPointPosition;
        private Vector3 prevEndPointPosition;
        private float prevMidPointPosition;

        private float prevLineQuality;
        private float prevRopeWidth;
        private float prevRopeLength;


        public bool IsPrefab => gameObject.scene.rootCount == 0;

        private void Start()
        {
            InitializeLineRenderer();
            if (AreEndPointsValid())
            {
                RecalculateRope();
            }
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                InitializeLineRenderer();
                if (AreEndPointsValid())
                {
                    RecalculateRope();
                }
                else
                {
                    lineRenderer.positionCount = 0;
                }
            }
        }

        private void OnDisable()
        {
            DestroyRopeEntity();
        }

        private void InitializeLineRenderer()
        {
            if (!lineRenderer)
            {
                lineRenderer = GetComponent<LineRenderer>();
            }

            lineRenderer.startWidth = ropeWidth;
            lineRenderer.endWidth = ropeWidth;
        }

        private void Update()
        {
            if (IsPrefab)
            {
                return;
            }

            if (AreEndPointsValid())
            {
                if (Application.isPlaying)
                {
                    if (particlePositions.Length != linePoints + 1 || !HasRopeEntity())
                    {
                        RecalculateRope();
                    }

                    WriteSimulationInput();
                }
                else if (IsPointsMoved() || IsRopeSettingsChanged())
                {
                    RecalculateRope();
                    NotifyPointsChanged();
                }

                prevStartPointPosition = startPoint.position;
                prevEndPointPosition = endPoint.position;
                prevMidPointPosition = midPointPosition;

                prevLineQuality = linePoints;
                prevRopeWidth = ropeWidth;
                prevRopeLength = ropeLength;
            }
        }

        /// <summary>
        /// Reads the particles in LateUpdate because the ECS simulation group runs at the end of the Update phase,
        /// after every MonoBehaviour Update.
        /// </summary>
        private void LateUpdate()
        {
            if (IsPrefab || !Application.isPlaying || !AreEndPointsValid() || !HasRopeEntity())
            {
                return;
            }

            ReadSimulationOutput();
            ApplyParticlePositions();
            NotifyPointsChanged();
        }

        private bool AreEndPointsValid()
        {
            return startPoint != null && endPoint != null;
        }

        /// <summary>
        /// Lays the particles on a quadratic Bezier sagging by the rope length in excess of the end point distance.
        /// This is the edit mode shape and the starting shape of the simulation.
        /// </summary>
        private void LayOutParticles()
        {
            if (particlePositions.Length != linePoints + 1)
            {
                particlePositions = new Vector3[linePoints + 1];
            }

            Vector3 start = startPoint.position;
            Vector3 end = endPoint.position;
            Vector3 control = Vector3.Lerp(start, end, 0.5f);
            control.y -= ropeLength - Mathf.Min(Vector3.Distance(start, end), ropeLength);

            for (int i = 0; i <= linePoints; i++)
            {
                float t = i / (float)linePoints;
                particlePositions[i] = (1 - t) * (1 - t) * start + 2 * (1 - t) * t * control + t * t * end;
            }
        }

        private void ApplyParticlePositions()
        {
            int renderedCount = (particlePositions.Length - 1) * smoothingSubdivisions + 1;
            if (renderedPositions.Length != renderedCount)
            {
                renderedPositions = new Vector3[renderedCount];
            }

            for (int i = 0; i < renderedCount; i++)
            {
                renderedPositions[i] = GetPointAt(i / (float)(renderedCount - 1));
            }

            if (lineRenderer.positionCount != renderedCount)
            {
                lineRenderer.positionCount = renderedCount;
            }

            lineRenderer.SetPositions(renderedPositions);

            if (midPoint != null)
            {
                midPoint.position = GetPointAt(midPointPosition);
            }
        }

        public Vector3 GetPointAt(float t)
        {
            if (!AreEndPointsValid())
            {
                Debug.LogError("StartPoint or EndPoint is not assigned.", gameObject);
                return Vector3.zero;
            }

            if (particlePositions.Length < 2)
            {
                return Vector3.Lerp(startPoint.position, endPoint.position, t);
            }

            float scaledT = Mathf.Clamp01(t) * (particlePositions.Length - 1);
            int index = Mathf.Min((int)scaledT, particlePositions.Length - 2);
            return EvaluateCatmullRom(index, scaledT - index);
        }

        /// <summary>
        /// Uniform Catmull-Rom curve between particles <paramref name="index"/> and <paramref name="index"/> + 1, so the
        /// rope passes through every simulated particle without corners. Uniform is enough because the length
        /// constraint keeps segments close to equal. Neighbours past the ends are mirrored so the ends keep the
        /// direction of their segment.
        /// </summary>
        private Vector3 EvaluateCatmullRom(int index, float u)
        {
            Vector3 p1 = particlePositions[index];
            Vector3 p2 = particlePositions[index + 1];
            Vector3 p0 = index > 0 ? particlePositions[index - 1] : 2f * p1 - p2;
            Vector3 p3 = index + 2 < particlePositions.Length ? particlePositions[index + 2] : 2f * p2 - p1;

            float u2 = u * u;
            float u3 = u2 * u;
            return 0.5f * (2f * p1
                           + (p2 - p0) * u
                           + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2
                           + (3f * p1 - p0 - 3f * p2 + p3) * u3);
        }

        private static bool TryGetEntityManager(out EntityManager entityManager)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            bool isWorldValid = world != null && world.IsCreated;
            entityManager = isWorldValid ? world.EntityManager : default;
            return isWorldValid;
        }

        private bool HasRopeEntity()
        {
            return ropeEntity != Entity.Null && TryGetEntityManager(out EntityManager entityManager) && entityManager.Exists(ropeEntity);
        }

        /// <summary>
        /// Restarts the simulation from <see cref="particlePositions"/>, creating the rope entity if needed.
        /// </summary>
        private void ResetRopeEntity()
        {
            if (!Application.isPlaying || IsPrefab || !TryGetEntityManager(out EntityManager entityManager))
            {
                return;
            }

            if (!HasRopeEntity())
            {
                ropeEntity = entityManager.CreateEntity(typeof(RopeSimulationSettings), typeof(RopeAnchors), typeof(RopeParticle));
            }

            DynamicBuffer<RopeParticle> particles = entityManager.GetBuffer<RopeParticle>(ropeEntity);
            particles.ResizeUninitialized(particlePositions.Length);
            for (int i = 0; i < particlePositions.Length; i++)
            {
                particles[i] = new RopeParticle { Position = particlePositions[i], PreviousPosition = particlePositions[i] };
            }

            WriteSimulationInput();
        }

        private void DestroyRopeEntity()
        {
            if (HasRopeEntity() && TryGetEntityManager(out EntityManager entityManager))
            {
                entityManager.DestroyEntity(ropeEntity);
            }

            ropeEntity = Entity.Null;
        }

        /// <summary>
        /// Converts <see cref="otherPhysicsFactors"/> to an acceleration because the ECS fixed step does not use
        /// <see cref="Time.fixedDeltaTime"/>.
        /// </summary>
        private void WriteSimulationInput()
        {
            if (!HasRopeEntity() || !TryGetEntityManager(out EntityManager entityManager))
            {
                return;
            }

            entityManager.SetComponentData(ropeEntity, new RopeSimulationSettings
            {
                SegmentLength = ropeLength / linePoints,
                Radius = ropeWidth * 0.5f,
                Damping = damping,
                ConstraintIterations = constraintIterations,
                Filter = new CollisionFilter
                {
                    BelongsTo = belongsTo.Value,
                    CollidesWith = collidesWith.Value,
                },
            });

            entityManager.SetComponentData(ropeEntity, new RopeAnchors
            {
                Start = startPoint.position,
                End = endPoint.position,
                ExternalAcceleration = otherPhysicsFactors / Time.fixedDeltaTime,
            });
        }

        private void ReadSimulationOutput()
        {
            if (!TryGetEntityManager(out EntityManager entityManager))
            {
                return;
            }

            DynamicBuffer<RopeParticle> particles = entityManager.GetBuffer<RopeParticle>(ropeEntity, true);
            if (particles.Length != particlePositions.Length)
            {
                return;
            }

            for (int i = 0; i < particles.Length; i++)
            {
                particlePositions[i] = particles[i].Position;
            }
        }

        // New API methods for setting start and end points
        // with instantAssign parameter to recalculate the rope immediately, without
        // animating the rope to the new position.
        // When newStartPoint or newEndPoint is null, the rope will be recalculated immediately

        public void SetStartPoint(Transform newStartPoint, bool instantAssign = false)
        {
            startPoint = newStartPoint;
            prevStartPointPosition = startPoint == null ? Vector3.zero : startPoint.position;

            if (instantAssign || newStartPoint == null)
            {
                RecalculateRope();
            }

            NotifyPointsChanged();
        }
        public void SetMidPoint(Transform newMidPoint, bool instantAssign = false)
        {
            midPoint = newMidPoint;
            prevMidPointPosition = midPoint == null ? 0.5f : midPointPosition;

            if (instantAssign || newMidPoint == null)
            {
                RecalculateRope();
            }
            NotifyPointsChanged();
        }

        public void SetEndPoint(Transform newEndPoint, bool instantAssign = false)
        {
            endPoint = newEndPoint;
            prevEndPointPosition = endPoint == null ? Vector3.zero : endPoint.position;

            if (instantAssign || newEndPoint == null)
            {
                RecalculateRope();
            }

            NotifyPointsChanged();
        }

        public void RecalculateRope()
        {
            if (!AreEndPointsValid())
            {
                lineRenderer.positionCount = 0;
                return;
            }

            LayOutParticles();
            ResetRopeEntity();
            ApplyParticlePositions();
        }

        private void NotifyPointsChanged()
        {
            OnPointsChanged?.Invoke();
        }

        private bool IsPointsMoved()
        {
            var startPointMoved = startPoint.position != prevStartPointPosition;
            var endPointMoved = endPoint.position != prevEndPointPosition;
            return startPointMoved || endPointMoved;
        }

        private bool IsRopeSettingsChanged()
        {
            var lineQualityChanged = !Mathf.Approximately(linePoints, prevLineQuality);
            var ropeWidthChanged = !Mathf.Approximately(ropeWidth, prevRopeWidth);
            var ropeLengthChanged = !Mathf.Approximately(ropeLength, prevRopeLength);
            var midPointPositionChanged = !Mathf.Approximately(midPointPosition, prevMidPointPosition);

            return lineQualityChanged
                   || ropeWidthChanged
                   || ropeLengthChanged
                   || midPointPositionChanged;
        }
    }
}
