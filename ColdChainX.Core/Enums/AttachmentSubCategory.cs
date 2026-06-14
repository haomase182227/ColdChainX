namespace ColdChainX.Core.Enums
{
    public enum AttachmentSubCategory
    {
        // Operational Documents
        DELIVERY_NOTE = 0,
        PACKING_LIST = 1,
        INVOICE = 2,
        VAT_INVOICE = 3,
        WAREHOUSE_RECEIPT_NOTE = 4,
        WAREHOUSE_ISSUE_NOTE = 5,
        HANDOVER_REPORT = 6,

        // Compliance Documents
        FOOD_SAFETY_CERTIFICATE = 7,
        QUARANTINE_CERTIFICATE = 8,
        COA_CERTIFICATE = 9,
        PRODUCT_LICENSE = 10,
        BATCH_RELEASE_CERTIFICATE = 11,
        CUSTOMS_DECLARATION = 12,
        IMPORT_PERMIT = 13,
        CERTIFICATE_OF_ORIGIN = 14,
        PLANT_QUARANTINE_CERTIFICATE = 15,
        VIETGAP_CERTIFICATE = 16,

        // Quality Documents
        QC_REPORT = 17,
        DAMAGE_REPORT = 18,
        TEMPERATURE_LOG = 19,
        TEMPERATURE_EXCEPTION_REPORT = 20,

        // Incident Documents
        DISPUTE_REPORT = 21,

        // Disposal Documents
        DISPOSAL_REPORT = 22,
        DESTRUCTION_CERTIFICATE = 23,

        // Evidence Photos
        VEHICLE_PHOTO = 24,
        SEAL_PHOTO = 25,
        TEMPERATURE_PHOTO = 26,
        GOODS_CONDITION_PHOTO = 27,
        DAMAGE_PHOTO = 28,
        BARCODE_PHOTO = 29,
        BATCH_PHOTO = 30,
        EXPIRY_DATE_PHOTO = 31,
        HANDOVER_PHOTO = 32
    }
}
