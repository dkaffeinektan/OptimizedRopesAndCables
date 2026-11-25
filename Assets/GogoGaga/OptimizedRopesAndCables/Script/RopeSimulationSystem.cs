namespace GogoGaga.OptimizedRopesAndCables
{
	using Unity.Burst;
	using Unity.Collections;
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Physics;
	using Unity.Physics.Systems;

	/// <summary>
	/// Verlet rope simulation. Runs after <see cref="PhysicsSystemGroup"/> so particles are pushed out of the colliders
	/// at their poses for this step. Collision is one-way: the rope never applies forces to rigid bodies.
	/// </summary>
	[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
	[UpdateAfter(typeof(PhysicsSystemGroup))]
	[BurstCompile]
	public partial struct RopeSimulationSystem : ISystem
	{
		/// <summary>Below this length a segment has no usable direction and is left as is.</summary>
		private const float MIN_SEGMENT_LENGTH = 1e-6f;

		[BurstCompile]
		public void OnCreate(ref SystemState state)
		{
			state.RequireForUpdate<PhysicsWorldSingleton>();
			state.RequireForUpdate<RopeSimulationSettings>();
		}

		[BurstCompile]
		public void OnUpdate(ref SystemState state)
		{
			float3 gravity = PhysicsStep.Default.Gravity;
			if (SystemAPI.TryGetSingleton(out PhysicsStep physicsStep))
			{
				gravity = physicsStep.Gravity;
			}

			RopeSimulationJob job = new RopeSimulationJob
			{
				CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
				Gravity = gravity,
				DeltaTime = SystemAPI.Time.DeltaTime,
			};
			job.ScheduleParallel();
		}

		[BurstCompile]
		private partial struct RopeSimulationJob : IJobEntity
		{
			[ReadOnly] public CollisionWorld CollisionWorld;
			public float3 Gravity;
			public float DeltaTime;

			private void Execute(in RopeSimulationSettings settings, in RopeAnchors anchors, DynamicBuffer<RopeParticle> particles)
			{
				int lastIndex = particles.Length - 1;
				if (lastIndex < 1)
				{
					return;
				}

				Integrate(settings, anchors, particles, lastIndex);

				for (int iteration = 0; iteration < settings.ConstraintIterations; iteration++)
				{
					SolveSegmentLengths(settings.SegmentLength, particles, lastIndex);
					ResolveCollisions(settings, particles, lastIndex);
				}
			}

			/// <summary>
			/// Moves free particles by their implicit velocity plus gravity and external accelerations, then pins both
			/// ends with no velocity so a teleported anchor does not fling the rope.
			/// </summary>
			private void Integrate(in RopeSimulationSettings settings, in RopeAnchors anchors, DynamicBuffer<RopeParticle> particles, int lastIndex)
			{
				float damping = math.max(0.0f, 1.0f - settings.Damping * DeltaTime);
				float3 displacement = (Gravity + anchors.ExternalAcceleration) * DeltaTime * DeltaTime;

				for (int i = 1; i < lastIndex; i++)
				{
					ref RopeParticle particle = ref particles.ElementAt(i);
					float3 velocity = (particle.Position - particle.PreviousPosition) * damping;
					particle.PreviousPosition = particle.Position;
					particle.Position += velocity + displacement;
				}

				particles[0] = new RopeParticle { Position = anchors.Start, PreviousPosition = anchors.Start };
				particles[lastIndex] = new RopeParticle { Position = anchors.End, PreviousPosition = anchors.End };
			}

			/// <summary>
			/// Moves each pair of neighbours back to the rest length. Pinned ends have no inverse mass, so their
			/// neighbour takes the whole correction.
			/// </summary>
			private static void SolveSegmentLengths(float segmentLength, DynamicBuffer<RopeParticle> particles, int lastIndex)
			{
				for (int i = 0; i < lastIndex; i++)
				{
					ref RopeParticle a = ref particles.ElementAt(i);
					ref RopeParticle b = ref particles.ElementAt(i + 1);

					float weightA = i == 0 ? 0.0f : 1.0f;
					float weightB = i + 1 == lastIndex ? 0.0f : 1.0f;
					float weightSum = weightA + weightB;
					float3 delta = b.Position - a.Position;
					float length = math.length(delta);
					if (weightSum == 0.0f || length < MIN_SEGMENT_LENGTH)
					{
						continue;
					}

					float3 correction = delta * ((length - segmentLength) / (length * weightSum));
					a.Position += correction * weightA;
					b.Position -= correction * weightB;
				}
			}

			/// <summary>
			/// Places every free particle closer than the rope radius to a collider back on the collider surface,
			/// offset by the radius along the surface normal.
			/// </summary>
			private void ResolveCollisions(in RopeSimulationSettings settings, DynamicBuffer<RopeParticle> particles, int lastIndex)
			{
				for (int i = 1; i < lastIndex; i++)
				{
					ref RopeParticle particle = ref particles.ElementAt(i);
					PointDistanceInput input = new PointDistanceInput
					{
						Position = particle.Position,
						MaxDistance = settings.Radius,
						Filter = settings.Filter,
					};

					if (CollisionWorld.CalculateDistance(input, out DistanceHit hit))
					{
						particle.Position = hit.Position + hit.SurfaceNormal * settings.Radius;
					}
				}
			}
		}
	}
}
