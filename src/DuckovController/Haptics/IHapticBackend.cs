namespace DuckovController.Haptics
{
    internal interface IHapticBackend
    {
        void SetMotors(float low, float high);
        void Reset();
    }
}
