namespace ChatSupport.Tests
{
    
    public interface IDateTimeProvider
    {
        int GetCurrentShift();
        bool IsDuringOfficeHours();
    }
    
}