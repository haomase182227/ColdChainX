namespace ColdChainX.Core.Enums;

public enum LpnState
{
    EXPECTED          = 0,
    RECEIVING         = 1,
    DISCREPANCY_HOLD  = 2,
    RETURN_PENDING    = 3,
    IN_STOCK          = 4,
    ALLOCATED         = 5,
    LOADING           = 6,
    LOADING_COMPLETED = 9,
    RELEASED          = 7,
    SHIPPING          = 8,
    DELETED           = 10,
    DELIVERED         = 11,
    DELIVERY_RETURNED = 12,
    RECEIVED_AT_HUB   = 13,
    PENDING_REDELIVERY = 14
}
