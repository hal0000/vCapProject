namespace Interface
{
    public interface IAnimatable
    {
        bool IsAnimating { get; }
        bool IsActive { get; }
        void StartAnimation();
        void StopAnimation();
        void OnParentVisibilityChanged(bool isVisible);
    }
}