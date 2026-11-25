namespace GogoGaga.OptimizedRopesAndCables
{
	using Unity.Entities;
	using Unity.Mathematics;
	using Unity.Physics;

	/// <summary>
	/// Per-rope simulation parameters, written by <see cref="Rope"/> every frame so inspector edits apply at runtime.
	/// </summary>
	public struct RopeSimulationSettings : IComponentData
	{
		public float SegmentLength;
		public float Radius;
		public float Damping;
		public int ConstraintIterations;
		/// <summary>Filter used for the point distance queries that push particles out of colliders.</summary>
		public CollisionFilter Filter;
	}

	/// <summary>
	/// World-space data the rope receives from its GameObject side: the pinned end positions and external
	/// accelerations such as wind.
	/// </summary>
	public struct RopeAnchors : IComponentData
	{
		public float3 Start;
		public float3 End;
		public float3 ExternalAcceleration;
	}

	/// <summary>
	/// One Verlet particle of the rope. The first and last elements are pinned to <see cref="RopeAnchors"/>.
	/// </summary>
	[InternalBufferCapacity(0)]
	public struct RopeParticle : IBufferElementData
	{
		public float3 Position;
		public float3 PreviousPosition;
	}
}
