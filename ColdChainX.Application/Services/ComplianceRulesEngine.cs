using System;
using System.Collections.Generic;
using System.Linq;
using ColdChainX.Core.Entities;
using ColdChainX.Core.Enums;
using ColdChainX.Application.Models;

namespace ColdChainX.Application.Services
{
    public class ComplianceRulesEngine
    {
        public ComplianceCheckResult ValidateReceipt(WarehouseReceipt receipt, IEnumerable<WarehouseEvidenceAttachment> attachments)
        {
            var result = new ComplianceCheckResult();

            if (receipt == null)
            {
                result.FailedRequirements.Add("Receipt cannot be null.");
                result.Passed = false;
                return result;
            }

            var items = receipt.WarehouseReceiptItems ?? new List<WarehouseReceiptItem>();
            var attachmentList = attachments?.ToList() ?? new List<WarehouseEvidenceAttachment>();

            // A. Product Category Rules
            foreach (var item in items)
            {
                var category = item.ProductCategory;

                // FOOD rules
                if (category == ProductCategory.FOOD)
                {
                    if (item.ManufacturedDate == null)
                    {
                        result.MissingRequirements.Add($"FOOD item '{item.ItemName}': ManufacturedDate is required.");
                    }
                    if (item.ExpiryDate == null)
                    {
                        result.MissingRequirements.Add($"FOOD item '{item.ItemName}': ExpiryDate is required.");
                    }
                }

                // SEAFOOD rules
                if (category == ProductCategory.SEAFOOD)
                {
                    if (item.ManufacturedDate == null)
                    {
                        result.MissingRequirements.Add($"SEAFOOD item '{item.ItemName}': ManufacturedDate is required.");
                    }
                    if (item.ExpiryDate == null)
                    {
                        result.MissingRequirements.Add($"SEAFOOD item '{item.ItemName}': ExpiryDate is required.");
                    }
                    
                    // QUARANTINE_CERTIFICATE required
                    CheckAttachmentRequirement(
                        AttachmentSubCategory.QUARANTINE_CERTIFICATE, 
                        attachmentList, 
                        $"SEAFOOD item '{item.ItemName}'", 
                        result
                    );
                }

                // AGRICULTURE rules
                if (category == ProductCategory.AGRICULTURE)
                {
                    if (string.IsNullOrWhiteSpace(item.CountryOfOrigin))
                    {
                        result.MissingRequirements.Add($"AGRICULTURE item '{item.ItemName}': CountryOfOrigin is required.");
                    }
                }

                // PHARMA rules
                if (category == ProductCategory.PHARMA)
                {
                    if (item.ManufacturedDate == null)
                    {
                        result.MissingRequirements.Add($"PHARMA item '{item.ItemName}': ManufacturedDate is required.");
                    }
                    if (item.ExpiryDate == null)
                    {
                        result.MissingRequirements.Add($"PHARMA item '{item.ItemName}': ExpiryDate is required.");
                    }

                    // COA_CERTIFICATE required
                    CheckAttachmentRequirement(
                        AttachmentSubCategory.COA_CERTIFICATE, 
                        attachmentList, 
                        $"PHARMA item '{item.ItemName}'", 
                        result
                    );
                }

                // VACCINE rules
                if (category == ProductCategory.VACCINE)
                {
                    if (item.ManufacturedDate == null)
                    {
                        result.MissingRequirements.Add($"VACCINE item '{item.ItemName}': ManufacturedDate is required.");
                    }
                    if (item.ExpiryDate == null)
                    {
                        result.MissingRequirements.Add($"VACCINE item '{item.ItemName}': ExpiryDate is required.");
                    }

                    // COA_CERTIFICATE required
                    CheckAttachmentRequirement(
                        AttachmentSubCategory.COA_CERTIFICATE, 
                        attachmentList, 
                        $"VACCINE item '{item.ItemName}'", 
                        result
                    );

                    // PRODUCT_LICENSE required
                    CheckAttachmentRequirement(
                        AttachmentSubCategory.PRODUCT_LICENSE, 
                        attachmentList, 
                        $"VACCINE item '{item.ItemName}'", 
                        result
                    );
                }
            }

            // B. Import Rules
            // Determine import status using:
            // isImport = ProductCategory == IMPORT_GOODS OR CountryOfOrigin != "Vietnam"
            bool isImport = items.Any(item => 
                item.ProductCategory == ProductCategory.IMPORT_GOODS || 
                (!string.IsNullOrWhiteSpace(item.CountryOfOrigin) && !item.CountryOfOrigin.Trim().Equals("Vietnam", StringComparison.OrdinalIgnoreCase))
            );

            if (isImport)
            {
                // Mandatory: CUSTOMS_DECLARATION, CERTIFICATE_OF_ORIGIN
                CheckAttachmentRequirement(
                    AttachmentSubCategory.CUSTOMS_DECLARATION,
                    attachmentList,
                    "Import Goods",
                    result
                );

                CheckAttachmentRequirement(
                    AttachmentSubCategory.CERTIFICATE_OF_ORIGIN,
                    attachmentList,
                    "Import Goods",
                    result
                );

                // Conditional: SEAL_PHOTO
                CheckSealPhotoRequirement(attachmentList, result);
            }

            // Final Result Evaluation
            result.Passed = result.MissingRequirements.Count == 0 && 
                            result.FailedRequirements.Count == 0 && 
                            result.PendingRequirements.Count == 0;

            return result;
        }

        private void CheckAttachmentRequirement(
            AttachmentSubCategory subCategory, 
            List<WarehouseEvidenceAttachment> attachments, 
            string context, 
            ComplianceCheckResult result)
        {
            var matching = attachments.Where(a => a.SubCategory == subCategory).ToList();

            if (matching.Count == 0)
            {
                result.MissingRequirements.Add($"{context}: Missing mandatory attachment '{subCategory}'.");
                return;
            }

            // 1. VERIFIED -> satisfies requirement
            bool hasVerified = matching.Any(a => a.Status == DocumentStatus.VERIFIED);
            if (hasVerified)
            {
                return; // Satisfied
            }

            // 2. PENDING -> PendingRequirements (If any matching attachment is PENDING, classify as PendingRequirements)
            bool hasPending = matching.Any(a => a.Status == DocumentStatus.PENDING);
            if (hasPending)
            {
                result.PendingRequirements.Add($"{context}: Mandatory attachment '{subCategory}' is PENDING verification.");
                return;
            }

            // 3. REJECTED -> FailedRequirements (Only classify as FailedRequirements when all matching attachments are REJECTED)
            bool allRejected = matching.All(a => a.Status == DocumentStatus.REJECTED);
            if (allRejected)
            {
                result.FailedRequirements.Add($"{context}: Mandatory attachment '{subCategory}' was REJECTED.");
                return;
            }

            // 4. MISSING/Fallback -> MissingRequirements
            result.MissingRequirements.Add($"{context}: Missing mandatory attachment '{subCategory}'.");
        }

        private void CheckSealPhotoRequirement(
            List<WarehouseEvidenceAttachment> attachments, 
            ComplianceCheckResult result)
        {
            var sealPhotos = attachments.Where(a => a.SubCategory == AttachmentSubCategory.SEAL_PHOTO).ToList();

            if (sealPhotos.Count == 0)
            {
                result.MissingRequirements.Add("Import Goods: Missing SEAL_PHOTO.");
                return;
            }

            // 1. VERIFIED with valid SealNumber -> satisfies requirement
            bool hasVerifiedWithValidSealNumber = sealPhotos.Any(p => 
                p.Status == DocumentStatus.VERIFIED && 
                !string.IsNullOrWhiteSpace(p.SealNumber)
            );
            if (hasVerifiedWithValidSealNumber)
            {
                return; // Satisfied!
            }

            // 2. PENDING -> PendingRequirements (If any matching attachment is PENDING, classify as PendingRequirements)
            bool hasPending = sealPhotos.Any(p => p.Status == DocumentStatus.PENDING);
            if (hasPending)
            {
                result.PendingRequirements.Add("Import Goods: SEAL_PHOTO is PENDING verification.");
                return;
            }

            // 3. VERIFIED but missing SealNumber -> FailedRequirements
            bool hasVerifiedWithoutSealNumber = sealPhotos.Any(p => p.Status == DocumentStatus.VERIFIED);
            if (hasVerifiedWithoutSealNumber)
            {
                result.FailedRequirements.Add("Import Goods: SEAL_PHOTO verified but is missing a valid SealNumber.");
                return;
            }

            // 4. REJECTED -> FailedRequirements (Only classify as FailedRequirements when all matching attachments are REJECTED)
            bool allRejected = sealPhotos.All(p => p.Status == DocumentStatus.REJECTED);
            if (allRejected)
            {
                result.FailedRequirements.Add("Import Goods: SEAL_PHOTO was REJECTED.");
                return;
            }

            // 5. Fallback/MISSING -> MissingRequirements
            result.MissingRequirements.Add("Import Goods: Missing SEAL_PHOTO.");
        }

        public ComplianceCheckResult ValidateOutboundOrder(
            OutboundOrder order, 
            IEnumerable<WarehouseEvidenceAttachment> attachments,
            IDictionary<string, List<ProductCategory>> itemCategories)
        {
            var result = new ComplianceCheckResult();

            if (order == null)
            {
                result.FailedRequirements.Add("Order cannot be null.");
                result.Passed = false;
                return result;
            }

            var attachmentList = attachments?.ToList() ?? new List<WarehouseEvidenceAttachment>();
            var items = order.OutboundOrderItems ?? new List<OutboundOrderItem>();

            // Collect product categories for the order
            var activeCategories = new HashSet<ProductCategory>();

            // 1. Safeguard check: Check for ProductCategory ambiguity
            foreach (var item in items)
            {
                if (!itemCategories.TryGetValue(item.ItemCode, out var categories) || !categories.Any())
                {
                    result.FailedRequirements.Add($"Item '{item.ItemName}' (Code: {item.ItemCode}) does not have a resolved product category.");
                    continue;
                }

                if (categories.Count > 1)
                {
                    // Inconsistency detected: more than one category mapped to this ItemCode
                    result.FailedRequirements.Add($"Ambiguous product category for item '{item.ItemName}' (Code: {item.ItemCode}). Resolved categories: {string.Join(", ", categories)}.");
                    result.Passed = false;
                    return result;
                }

                activeCategories.Add(categories.First());
            }

            if (result.FailedRequirements.Any())
            {
                result.Passed = false;
                return result;
            }

            // Technical debt note:
            // Future Refactor:
            // Persist ProductCategory directly in InventoryStock and remove dependency on WarehouseReceiptItems.

            // 2. General Rules:
            // VERIFIED WAREHOUSE_ISSUE_NOTE is mandatory.
            CheckAttachmentRequirement(
                AttachmentSubCategory.WAREHOUSE_ISSUE_NOTE,
                attachmentList,
                "Outbound Order General",
                result
            );

            // 3. Category-Specific Rules:
            // FOOD / SEAFOOD:
            // - VERIFIED GOODS_CONDITION_PHOTO
            // - VERIFIED (TEMPERATURE_PHOTO OR TEMPERATURE_LOG)
            if (activeCategories.Contains(ProductCategory.FOOD) || activeCategories.Contains(ProductCategory.SEAFOOD))
            {
                CheckAttachmentRequirement(
                    AttachmentSubCategory.GOODS_CONDITION_PHOTO,
                    attachmentList,
                    "Outbound FOOD/SEAFOOD",
                    result
                );

                CheckOutboundTemperatureRequirement(attachmentList, "Outbound FOOD/SEAFOOD", result);
            }

            // PHARMA / VACCINE:
            // - VERIFIED (QC_REPORT OR BATCH_RELEASE_CERTIFICATE)
            // - VERIFIED TEMPERATURE_LOG
            if (activeCategories.Contains(ProductCategory.PHARMA) || activeCategories.Contains(ProductCategory.VACCINE))
            {
                CheckOutboundPharmaCertificateRequirement(attachmentList, "Outbound PHARMA/VACCINE", result);

                CheckAttachmentRequirement(
                    AttachmentSubCategory.TEMPERATURE_LOG,
                    attachmentList,
                    "Outbound PHARMA/VACCINE",
                    result
                );
            }

            // Final Result Evaluation
            result.Passed = result.MissingRequirements.Count == 0 && 
                            result.FailedRequirements.Count == 0 && 
                            result.PendingRequirements.Count == 0;

            return result;
        }

        private void CheckOutboundTemperatureRequirement(
            List<WarehouseEvidenceAttachment> attachments, 
            string context, 
            ComplianceCheckResult result)
        {
            var tempPhotos = attachments.Where(a => a.SubCategory == AttachmentSubCategory.TEMPERATURE_PHOTO).ToList();
            var tempLogs = attachments.Where(a => a.SubCategory == AttachmentSubCategory.TEMPERATURE_LOG).ToList();

            var combined = tempPhotos.Concat(tempLogs).ToList();

            if (combined.Count == 0)
            {
                result.MissingRequirements.Add($"{context}: Missing mandatory TEMPERATURE_PHOTO or TEMPERATURE_LOG.");
                return;
            }

            // 1. If any is VERIFIED, requirement is satisfied
            if (combined.Any(a => a.Status == DocumentStatus.VERIFIED))
            {
                return;
            }

            // 2. If any is PENDING, requirement is PENDING
            if (combined.Any(a => a.Status == DocumentStatus.PENDING))
            {
                result.PendingRequirements.Add($"{context}: TEMPERATURE_PHOTO or TEMPERATURE_LOG is PENDING verification.");
                return;
            }

            // 3. If all are REJECTED, requirement failed
            if (combined.All(a => a.Status == DocumentStatus.REJECTED))
            {
                result.FailedRequirements.Add($"{context}: All uploaded TEMPERATURE_PHOTO and TEMPERATURE_LOG attachments were REJECTED.");
                return;
            }

            // 4. Fallback
            result.MissingRequirements.Add($"{context}: Missing mandatory TEMPERATURE_PHOTO or TEMPERATURE_LOG.");
        }

        private void CheckOutboundPharmaCertificateRequirement(
            List<WarehouseEvidenceAttachment> attachments, 
            string context, 
            ComplianceCheckResult result)
        {
            var qcReports = attachments.Where(a => a.SubCategory == AttachmentSubCategory.QC_REPORT).ToList();
            var batchCerts = attachments.Where(a => a.SubCategory == AttachmentSubCategory.BATCH_RELEASE_CERTIFICATE).ToList();

            var combined = qcReports.Concat(batchCerts).ToList();

            if (combined.Count == 0)
            {
                result.MissingRequirements.Add($"{context}: Missing mandatory QC_REPORT or BATCH_RELEASE_CERTIFICATE.");
                return;
            }

            // 1. If any is VERIFIED, requirement is satisfied
            if (combined.Any(a => a.Status == DocumentStatus.VERIFIED))
            {
                return;
            }

            // 2. If any is PENDING, requirement is PENDING
            if (combined.Any(a => a.Status == DocumentStatus.PENDING))
            {
                result.PendingRequirements.Add($"{context}: QC_REPORT or BATCH_RELEASE_CERTIFICATE is PENDING verification.");
                return;
            }

            // 3. If all are REJECTED, requirement failed
            if (combined.All(a => a.Status == DocumentStatus.REJECTED))
            {
                result.FailedRequirements.Add($"{context}: All uploaded QC_REPORT and BATCH_RELEASE_CERTIFICATE attachments were REJECTED.");
                return;
            }

            // 4. Fallback
            result.MissingRequirements.Add($"{context}: Missing mandatory QC_REPORT or BATCH_RELEASE_CERTIFICATE.");
        }
    }
}
