# AIRTS Development Rules

These rules apply to future work in this repository.

## Lockstep Logic Rules

- Logic-layer code must use fixed-point math.
- Use `Fix64`, `FixedVector2`, `FixedVector3`, `FixedMath`, and `AIRTS.Lockstep.Physics` for simulation values.
- Do not use Unity `float`, `double`, `Vector2`, `Vector3`, `Time.deltaTime`, `Random`, Unity Physics, or `NavMeshAgent` to produce lockstep simulation results.
- Rendering code may convert fixed-point snapshots to Unity values for display only.
- Inputs must become deterministic lockstep commands first; gameplay state changes should be applied from lockstep frames, not directly from Unity input callbacks.

## Movement Rules

- Character movement must use navigation mesh pathfinding.
- Use `NavMeshQuery.TryFindPath` to create movement paths.
- Units may move along an existing path with fixed-point steps.
- Direct line movement is only allowed after the path system has proven that the segment is valid and walkable.
- Local avoidance may use `FixedCollisionAvoidance`, but it must not let characters leave walkable navigation space.

## Documentation

See `Docs/LockstepGameplayGuide.md` before adding gameplay logic, AI, movement, commands, or simulation systems.
