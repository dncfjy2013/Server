using OpenTK.Mathematics;

namespace FbxGenerator.Physics
{
    public static class CollisionDetector
    {
        public static bool CheckCollision(Collider a, Collider b, out CollisionInfo collisionInfo)
        {
            return a.CheckCollision(b, out collisionInfo);
        }

        public static bool CheckSphereSphereCollision(SphereCollider a, SphereCollider b, out CollisionInfo collisionInfo)
        {
            collisionInfo = new CollisionInfo();

            var centerA = a.GetCenter();
            var centerB = b.GetCenter();

            var distance = centerB - centerA;
            var distanceSquared = distance.LengthSquared;
            var radiusSum = a.Radius + b.Radius;

            if (distanceSquared >= radiusSum * radiusSum)
                return false;

            var distanceLength = MathHelper.Sqrt(distanceSquared);

            collisionInfo.Normal = distance / (float)(distanceLength > 0 ? distanceLength : 1.0f);
            collisionInfo.Penetration = (float)(radiusSum - distanceLength);
            collisionInfo.ContactPoint = centerA + collisionInfo.Normal * a.Radius;
            collisionInfo.Other = b;

            return true;
        }

        public static bool CheckBoxBoxCollision(BoxCollider a, BoxCollider b, out CollisionInfo collisionInfo)
        {
            collisionInfo = new CollisionInfo();

            var centerA = a.GetCenter();
            var centerB = b.GetCenter();

            var halfSizeA = a.Size * 0.5f;
            var halfSizeB = b.Size * 0.5f;

            // 计算AABB的最小和最大点
            var minA = centerA - halfSizeA;
            var maxA = centerA + halfSizeA;
            var minB = centerB - halfSizeB;
            var maxB = centerB + halfSizeB;

            // 检查是否在所有轴上都有重叠
            if (maxA.X < minB.X || minA.X > maxB.X ||
                maxA.Y < minB.Y || minA.Y > maxB.Y ||
                maxA.Z < minB.Z || minA.Z > maxB.Z)
            {
                return false;
            }

            // 计算重叠量
            var overlapX = System.Math.Min(maxA.X, maxB.X) - System.Math.Max(minA.X, minB.X);
            var overlapY = System.Math.Min(maxA.Y, maxB.Y) - System.Math.Max(minA.Y, minB.Y);
            var overlapZ = System.Math.Min(maxA.Z, maxB.Z) - System.Math.Max(minA.Z, minB.Z);

            // 找出最小重叠轴
            var minOverlap = System.Math.Min(overlapX, System.Math.Min(overlapY, overlapZ));

            if (minOverlap == overlapX)
            {
                collisionInfo.Normal = (centerB.X > centerA.X) ? Vector3.UnitX : -Vector3.UnitX;
                collisionInfo.Penetration = overlapX;
            }
            else if (minOverlap == overlapY)
            {
                collisionInfo.Normal = (centerB.Y > centerA.Y) ? Vector3.UnitY : -Vector3.UnitY;
                collisionInfo.Penetration = overlapY;
            }
            else
            {
                collisionInfo.Normal = (centerB.Z > centerA.Z) ? Vector3.UnitZ : -Vector3.UnitZ;
                collisionInfo.Penetration = overlapZ;
            }

            collisionInfo.Other = b;

            return true;
        }

        public static bool CheckSphereBoxCollision(SphereCollider sphere, BoxCollider box, out CollisionInfo collisionInfo)
        {
            collisionInfo = new CollisionInfo();

            var sphereCenter = sphere.GetCenter();
            var boxCenter = box.GetCenter();
            var boxHalfSize = box.Size * 0.5f;

            // 计算球体中心到盒子的最近点
            var closestPoint = new Vector3();
            closestPoint.X = System.Math.Max(boxCenter.X - boxHalfSize.X, System.Math.Min(sphereCenter.X, boxCenter.X + boxHalfSize.X));
            closestPoint.Y = System.Math.Max(boxCenter.Y - boxHalfSize.Y, System.Math.Min(sphereCenter.Y, boxCenter.Y + boxHalfSize.Y));
            closestPoint.Z = System.Math.Max(boxCenter.Z - boxHalfSize.Z, System.Math.Min(sphereCenter.Z, boxCenter.Z + boxHalfSize.Z));

            // 计算最近点到球体中心的向量
            var distance = closestPoint - sphereCenter;
            var distanceSquared = distance.LengthSquared;

            // 如果距离平方大于球体半径的平方，则没有碰撞
            if (distanceSquared > sphere.Radius * sphere.Radius)
                return false;

            // 计算碰撞法线和穿透深度
            var distanceLength = System.MathF.Sqrt(distanceSquared);
            collisionInfo.Normal = (distanceLength > 0) ? -distance / distanceLength : Vector3.UnitY; // 默认向上的法线
            collisionInfo.Penetration = sphere.Radius - distanceLength;
            collisionInfo.ContactPoint = closestPoint;
            collisionInfo.Other = box;

            return true;
        }
    }
}