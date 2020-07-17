namespace Il2CppModdingCodegen.Data
{
    public interface IAttribute
    {
        string Name { get; }
        int RVA { get; }
        int Offset { get; }
        int VA { get; }
    }
}
