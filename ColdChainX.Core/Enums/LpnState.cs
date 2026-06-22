namespace ColdChainX.Core.Enums;

public enum LpnState
{
    EXPECTED = 0,
    RECEIVING = 1,
    DISCREPANCY_HOLD = 2,
    RETURN_PENDING = 3,
    IN_STOCK = 4,
    ALLOCATED = 5,
    PICKED = 6,
    SHIPPED = 7
}
