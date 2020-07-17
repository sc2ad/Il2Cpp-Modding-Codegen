namespace Il2Cpp_Modding_Codegen.Data
{
    public interface IAttribute
    {
        string Name { get; }
        int RVA { get; }
        int Offset { get; }
        int VA { get; }
    }
}