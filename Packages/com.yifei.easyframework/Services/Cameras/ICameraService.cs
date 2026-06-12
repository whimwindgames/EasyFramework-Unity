namespace EasyFramework.Services.Cameras
{
    public interface ICameraService
    {
        void Follow(UnityEngine.Transform target);
        void SetBounds(UnityEngine.Bounds bounds);
        void Shake(float intensity, float duration);
    }
}
