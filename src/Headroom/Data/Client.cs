namespace Headroom.Data;

// The customer a Project is for. Name is unique without case.
public class Client : Record
{
    public string Name { get; set; } = "";
}
