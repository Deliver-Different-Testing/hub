namespace Hub.Models;

public class SaltHashed
{
    #region Properties

    public required string Salt { get; set; }

    public required string Hashed { get; set; }

    #endregion Properties
}