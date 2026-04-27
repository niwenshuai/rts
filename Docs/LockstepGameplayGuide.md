# AIRTS Lockstep Gameplay Guide

本文档记录当前项目已有的锁步、定点数、导航网格、行为树、物理数学工具的使用方式，后续写具体玩法逻辑时优先查这里。

## 开发硬规则

1. 逻辑层代码必须使用定点数计算。
2. 角色移动必须使用导航网格寻路移动。
3. Unity 的 `float`、`double`、`Vector2`、`Vector3`、`Time.deltaTime`、`Random`、`Physics`、`NavMeshAgent` 只能用于表现层、编辑器工具或输入转换，不能参与锁步逻辑结果。
4. 逻辑层坐标、速度、时间、距离、碰撞、AI 判断都使用 `Fix64`、`FixedVector2`、`FixedVector3`、`FixedMath`、`AIRTS.Lockstep.Physics`。
5. 表现层可以把定点结果转换成 Unity 坐标做渲染插值，但不能反向把渲染坐标作为逻辑真值。

推荐把后续玩法代码按职责放到锁步命名空间下，例如：

- `AIRTS.Lockstep.Simulation`：世界、实体、组件、系统、帧推进。
- `AIRTS.Lockstep.Gameplay`：技能、战斗、资源、建造、单位命令。
- `AIRTS.Lockstep.AI`：行为树装配、虚拟玩家、自动化操作。

## 模块总览

当前核心目录：

- `Assets/Scripts/Lockstep/Math`：定点数和定点向量。
- `Assets/Scripts/Lockstep/Shared`：网络消息、锁步帧、玩家命令协议。
- `Assets/Scripts/Lockstep/Client`：客户端 TCP 连接、帧缓冲。
- `Assets/Scripts/Lockstep/Server`：锁步服务器逻辑副本。
- `Server/AIRTS.Server`：独立控制台服务器。
- `Assets/Scripts/Lockstep/Unity`：Unity 客户端行为组件和定点/Unity 转换。
- `Assets/Scripts/Lockstep/Navigation`：定点导航网格查询。
- `Assets/Scripts/Lockstep/BehaviorTree`：定点友好的行为树。
- `Assets/Scripts/Lockstep/Physics`：定点物理数学、碰撞、射线、规避。

## 定点数基础

核心类型：

- `Fix64`：定点数，内部 `RawValue` 使用 `Scale = 10000`。
- `FixedVector2`：2D 定点向量，适合平面导航、规避、碰撞。
- `FixedVector3`：3D 定点向量，适合逻辑世界坐标。
- `FixedMath`：`Abs`、`Min`、`Max`、`Clamp`、`Lerp`、`Sqrt`、`MoveTowards`。

常用写法：

```csharp
Fix64 speed = Fix64.FromInt(5);
Fix64 dt = Fix64.One / Fix64.FromInt(30);
FixedVector2 dir = (target - position).Normalized;
position += dir * speed * dt;
```

Unity 输入或 Inspector 数据进入逻辑层时，需要转换成定点数。已有 `FixedUnityExtensions` 提供 Unity 向量转定点向量：

```csharp
FixedVector3 fixedPosition = unityPosition.ToFixedVector3();
```

注意：转换应发生在输入边界，逻辑内部不要继续使用 Unity 浮点坐标。

## 锁步网络流程

### 服务器启动

独立服务器入口：

```text
Server/AIRTS.Server/Program.cs
```

启动后监听默认端口 `7777`。服务器控制台命令：

- `status`：查看连接玩家数、ready 数、是否开始、当前帧。
- `start` / `force` / `forcestart` / `force-start`：强制开始游戏。
- `exit` / `quit` / `q` / `stop`：退出服务器。
- `help` / `?`：查看命令。

服务器默认要求 `2` 个客户端连接，并且所有已连接玩家都按 F5 ready 后才开始跑帧。开始前服务器不会广播逻辑帧。

### 客户端连接与准备

Unity 侧组件：

```text
Assets/Scripts/Lockstep/Unity/LockstepClientBehaviour.cs
```

Inspector 关键字段：

- `host`：服务器地址，默认 `127.0.0.1`。
- `port`：服务器端口，默认 `7777`。
- `connectOnAwake`：Awake 时自动连接。
- `sendSpaceAsTestCommand`：按 Space 发送测试命令。
- `logFrames`：客户端逐帧日志。
- `showDebugOverlay`：左上角 UI 调试面板。

客户端按 F5 后发送 ready。左上角调试 UI 会显示：

- 连接状态。
- 玩家数。
- ready 数。
- 本地 F5 状态。
- 游戏是否 running。
- 网络帧和逻辑帧。

### 帧推进

服务器按网络帧广播 `LockstepFrame`。客户端本地以逻辑帧消耗网络帧：

- 网络帧率：服务器默认 `15`。
- 客户端逻辑帧率：`30`。
- 当前设置下每个网络帧拆成 `2` 个逻辑帧。

玩法逻辑应订阅：

```csharp
lockstepClientBehaviour.LogicFrameReady += OnLogicFrameReady;
```

示例：

```csharp
private void OnLogicFrameReady(LockstepFrame frame)
{
    for (int i = 0; i < frame.Commands.Count; i++)
    {
        PlayerCommand command = frame.Commands[i];
        ApplyCommand(command);
    }

    simulation.Tick(frame.FrameIndex);
}
```

重要约束：

- 所有会影响逻辑结果的代码都必须只在锁步 Tick 中执行。
- 不要在 Unity `Update` 中直接修改逻辑世界。
- 输入只生成 `PlayerCommand`，真正执行等锁步帧到达后统一处理。

## 玩家命令协议

命令结构：

```csharp
public struct PlayerCommand
{
    public int Frame;
    public int PlayerId;
    public int CommandType;
    public int TargetId;
    public int X;
    public int Y;
    public int Z;
    public byte[] Payload;
}
```

建议约定：

- `CommandType`：命令类型，例如移动、攻击、释放技能、建造。
- `TargetId`：目标实体 id，没有目标时为 `0` 或 `-1`，保持全项目统一。
- `X/Y/Z`：定点坐标的 `RawValue`，不要传 float。
- `Payload`：复杂命令的附加参数。需要确定性序列化，字段顺序和字节序固定。

发送移动命令示意：

```csharp
FixedVector3 target = clickPosition.ToFixedVector3();
client.SendCommandAsync(
    commandType: MoveCommand,
    targetId: 0,
    x: (int)target.X.RawValue,
    y: (int)target.Y.RawValue,
    z: (int)target.Z.RawValue);
```

后续如果 `RawValue` 可能超过 `int`，应扩展命令协议为 `long` 坐标，不能改用 float。

## 导航网格移动

角色移动必须通过定点导航网格。

核心类型：

- `NavMeshData`：导航数据。
- `NavPolygon`：导航多边形，目前使用 `FixedBounds2` 作为区域边界。
- `NavMeshQuery`：查询路径。
- `DynamicObstacleSet` / `NavObstacle`：动态障碍。
- `NavMeshQueryFilter`：寻路过滤器，包含角色半径、忽略自身障碍、是否启用动态障碍。
- `NavMeshBakeSettings`：烘焙参数。

寻路 API：

```csharp
bool found = navQuery.TryFindPath(start, end, path);
```

带动态障碍的寻路 API：

```csharp
var filter = new NavMeshQueryFilter(unitRadius, unitId);
bool found = navQuery.TryFindPath(start, end, path, filter);
```

示例：

```csharp
List<FixedVector2> path = new List<FixedVector2>();
var filter = new NavMeshQueryFilter(unitRadius, unit.Id);
if (navQuery.TryFindPath(unit.Position2, targetPosition2, path, filter))
{
    unit.SetPath(path);
}
```

动态障碍推荐流程：

```csharp
var obstacles = new List<NavObstacle>();
obstacles.Add(NavObstacle.Circle(building.Id, building.Position2, buildingRadius));
obstacles.Add(NavObstacle.Circle(unit.Id, unit.Position2, unitRadius));
dynamicObstacleSet.ReplaceAll(obstacles);
```

`NavMeshQuery` 会按 `NavMeshQueryFilter.AgentRadius` 膨胀障碍，并自动忽略 `IgnoreObstacleId` 指向的自身实体。路径查询内部会做路径平滑，直线段检测会采样动态障碍，效果接近 Unity NavMesh 中动态障碍 carving + agent radius 的组合思路。移动单位不建议雕刻导航网格，应像 Unity `NavMeshAgent` 一样交给局部避让处理，避免多个单位互相把路径封死。

沿路径移动建议：

```csharp
Fix64 step = unit.Speed * fixedDeltaTime;
while (step > Fix64.Zero && unit.PathIndex < unit.Path.Count)
{
    FixedVector2 waypoint = unit.Path[unit.PathIndex];
    FixedVector2 delta = waypoint - unit.Position2;
    Fix64 distance = delta.Magnitude;

    if (distance <= step)
    {
        unit.Position2 = waypoint;
        step -= distance;
        unit.PathIndex++;
        continue;
    }

    unit.Position2 += delta / distance * step;
    step = Fix64.Zero;
}
```

移动规则：

- 新目标出现时先寻路，再保存路径。
- 每个逻辑帧只沿当前路径推进。
- 遇到动态障碍变化时重新寻路。
- 单位要攻击、采集、交付资源时，目标点应放在目标实体外圈的可达交互点，不要直接寻路到建筑或资源中心。
- 禁止角色用直线穿越不可走区域。
- Unity `NavMeshAgent` 不能参与逻辑移动。

## 行为树系统

目录：

```text
Assets/Scripts/Lockstep/BehaviorTree
```

核心类型：

- `BehaviorTree`：行为树入口。
- `BehaviorNode`：节点基类。
- `BehaviorTreeContext`：Tick 上下文，包含 ActorId、逻辑帧、定点时间、黑板、确定性随机、命令输出。
- `BehaviorBlackboard`：黑板，支持 `bool`、`int`、`Fix64`、`FixedVector2`、`FixedVector3`。
- `IBehaviorCommandSink`：行为树输出命令的接口。

节点类型：

- 组合节点：`SequenceNode`、`SelectorNode`、`ParallelNode`。
- 装饰节点：`InverterNode`、`SucceederNode`、`RepeatNode`、`CooldownNode`。
- 叶子节点：`ConditionNode`、`ActionNode`、`WaitNode`、`SetBlackboardNode`、`HasBlackboardValueNode`。
- 定点节点：`BlackboardBoolConditionNode`、`BlackboardFix64ConditionNode`、`FixedDistanceConditionNode`、`SendCommandNode`。

简单示例：有目标且距离够近就攻击，否则移动到目标点。

```csharp
BehaviorTree tree = new BehaviorTree(
    new SelectorNode(
        new SequenceNode(
            new HasBlackboardValueNode("TargetPosition"),
            new FixedDistanceConditionNode("SelfPosition", "TargetPosition", Fix64.FromInt(6)),
            new SendCommandNode(AttackCommand, "TargetId", "TargetPosition")
        ),
        new SequenceNode(
            new HasBlackboardValueNode("TargetPosition"),
            new SendCommandNode(MoveCommand, 0, "TargetPosition")
        )
    )
);
```

每帧 Tick：

```csharp
context.AdvanceFrame(logicFrame, fixedDeltaTime);
context.Blackboard.SetFixedVector3("SelfPosition", unit.Position);
context.Blackboard.SetFixedVector3("TargetPosition", target.Position);
context.Blackboard.SetInt("TargetId", target.Id);
tree.Tick(context);
```

虚拟玩家推荐方式：

1. 行为树只做决策。
2. 决策输出 `BehaviorCommand`。
3. 虚拟玩家把 `BehaviorCommand` 转成 `PlayerCommand`。
4. 最终仍走锁步命令流程，不直接改世界。

## 定点物理数学工具

目录：

```text
Assets/Scripts/Lockstep/Physics
```

基础类型：

- `FixedRay2` / `FixedRay3`
- `FixedAabb2` / `FixedAabb3`
- `FixedCircle`
- `FixedSphere`
- `FixedCapsule2`
- `FixedRaycastHit2` / `FixedRaycastHit3`

数学工具：

- `FixedPhysicsMath.Perpendicular`
- `FixedPhysicsMath.Cross`
- `FixedPhysicsMath.ClampMagnitude`
- `FixedPhysicsMath.ClampPoint`
- `FixedPhysicsMath.ClosestPointOnSegment`
- `FixedPhysicsMath.SqrDistancePointSegment`
- `FixedPhysicsMath.SegmentsIntersect`
- `FixedPhysicsMath.SqrDistanceSegmentSegment`

碰撞检测：

```csharp
bool blocked = FixedCollision.Intersects(unitCircle, obstacleAabb);
```

支持：

- AABB / AABB
- Circle / Circle
- Sphere / Sphere
- Circle / AABB
- Sphere / AABB
- Capsule2 / Circle
- Capsule2 / Capsule2
- Capsule2 / AABB

射线检测：

```csharp
var ray = new FixedRay2(origin, direction.Normalized);
if (FixedRaycast.Raycast(ray, bounds, maxDistance, out FixedRaycastHit2 hit))
{
    FixedVector2 hitPoint = hit.Point;
}
```

支持：

- Ray2 / AABB2
- Ray3 / AABB3
- Ray2 / Circle
- Ray3 / Sphere
- Ray2 / Capsule2

碰撞规避：

```csharp
FixedVector2 safeVelocity = FixedCollisionAvoidance.ChooseSafeVelocity(
    position,
    desiredVelocity,
    radius,
    maxSpeed,
    Fix64.FromInt(1),
    circleObstacles,
    aabbObstacles);
```

常用规避函数：

- `ResolveCirclePenetrations`：把圆形角色推出圆形障碍。
- `ComputeSeparation`：计算邻居分离方向。
- `SteerAwayFromCircleObstacles`：根据前方射线做圆形障碍规避。
- `ChooseSafeVelocity`：从候选方向中选择较安全速度。

使用建议：

- 导航网格负责大方向路径。
- 规避算法负责局部避让。
- 规避后的速度仍应保持在可走区域内；如果偏离路径过多，重新寻路。

## 当前 RTS 原型

当前已经实现一套最小 RTS 玩法原型：

- 逻辑层目录：`Assets/Scripts/Lockstep/Gameplay`
- 显示/输入层目录：`Assets/Scripts/Lockstep/Unity/Gameplay`
- 运行时自动入口：`RtsPrototypeBootstrap`
- Unity 控制器：`RtsPrototypeController`

原型规则：

- 两名玩家分别拥有一个主城、一个金矿、一个农民。
- 农民会自动在己方金矿和主城之间采集金币。
- 主城可以生产农民。
- 兵营可以生产士兵。
- 农民可以建造兵营和防御塔。
- 当前原型中建造采用立即放置：只检查地图范围、金币和与其它建筑/单位的碰撞；放置判定会忽略执行建造的农民自身。建筑使用与显示大小一致的正方形 AABB 足迹，单位仍使用圆形碰撞。
- 士兵和防御塔可以攻击敌方单位/建筑。
- 建筑和金矿会同步到 `DynamicObstacleSet`，导航寻路会绕开它们；单位之间作为软障碍处理，通过定点圆形局部规避、确定性通行权、帧末分离和目标外圈多槽位选择避免互相卡死。
- 一方所有建筑被摧毁后，另一方获胜。

操作方式：

- 左键点击己方建筑或单位进行选择。
- 左键拖框选择己方单位。
- 右键地图：命令选中单位移动。
- 右键敌方单位/建筑：命令选中单位攻击。
- 选中建筑后，屏幕下方命令栏显示可生产单位。
- 选中农民后，屏幕下方命令栏显示移动、攻击、建造兵营、建造防御塔。
- 点击建造按钮后，再左键点击地图放置建筑。

测试方式：

- 默认会运行时创建 `AIRTS RTS Prototype`，生成椭圆形测试地图和两个出生点。
- 如果没有连接锁步服务器，会启用离线测试推进，方便单机查看采矿和 UI。
- 一旦客户端连接锁步服务器，离线测试推进停止；按 F5 ready 后，正式按锁步帧重置并运行。
- 两客户端联机测试时，先启动 `Server/AIRTS.Server`，再打开两个客户端，两个客户端都按 F5 开始。

玩法命令：

- `RtsCommandType.Move`
- `RtsCommandType.Attack`
- `RtsCommandType.ProduceUnit`
- `RtsCommandType.BuildBuilding`

逻辑移动仍然走 `NavMeshQuery.TryFindPath`。测试地图由 `RtsTestMapFactory.CreateEllipseNavMesh` 在运行时生成椭圆形导航网格。

## 推荐玩法逻辑结构

一个逻辑帧推荐顺序：

1. 从 `LockstepFrame.Commands` 读取并排序/应用输入。
2. 将移动/攻击/技能命令写入实体意图。
3. AI/虚拟玩家行为树 Tick，生成新命令或意图。
4. 对需要移动的实体调用 `NavMeshQuery.TryFindPath` 或沿已有路径前进。
5. 使用 `FixedCollisionAvoidance` 做局部规避。
6. 使用 `FixedCollision` 做碰撞检测和必要的穿透修正。
7. 更新战斗、技能、资源、建造等系统。
8. 生成表现层可读取的快照。

示意：

```csharp
public void Tick(int logicFrame, LockstepFrame frame)
{
    ApplyCommands(frame.Commands);
    TickAi(logicFrame);
    TickMovement(logicFrame);
    TickCombat(logicFrame);
    BuildViewSnapshot();
}
```

## 表现层与逻辑层边界

逻辑层：

- 使用定点数。
- 按锁步帧推进。
- 可重复、确定性、无 Unity 运行时依赖。
- 输出世界快照。

表现层：

- 读取世界快照。
- 将 `FixedVector3` 转成 Unity `Vector3`。
- 做插值、动画、特效、音效、UI。
- 不反向影响逻辑结果。

## 常见坑

- 不要在逻辑层用 `DateTime`、`Stopwatch`、`Time.time`、`Time.deltaTime`。
- 不要在逻辑层用 `UnityEngine.Random` 或 `System.Random`，需要随机时使用确定性随机并固定种子。
- 不要遍历无序集合后直接影响逻辑结果；如果使用字典，输出前按稳定 key 排序。
- 不要在不同客户端根据本地渲染状态做逻辑判断。
- 不要让客户端提前执行输入导致世界分叉；输入必须等锁步帧统一应用。
- 不要让角色直线移动到目标点；必须通过导航网格路径。

## 后续扩展建议

- 增加 `SimulationWorld`，统一实体、组件、系统、逻辑帧入口。
- 增加 `CommandType` 常量或 enum，统一命令编号。
- 增加命令 payload 的确定性序列化工具。
- 增加导航网格资源加载和编辑器烘焙产物保存。
- 增加行为树装配工厂，把虚拟玩家策略集中管理。
- 增加逻辑回放和帧校验 hash，用于排查不同步。
