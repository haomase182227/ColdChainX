INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES
    ('20260608000100_AddOrderIdToCustomerContracts', '8.0.8'),
    ('20260609000100_AddOrderQuantityPackingAndQuotationPdf', '8.0.8'),
    ('20260609000200_SecurityPaginationAndLocationCleanup', '8.0.8')
ON CONFLICT ("MigrationId") DO NOTHING;
