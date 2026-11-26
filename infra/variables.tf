variable "location" {
  description = "Resource region. Defaults to the existing resource group's location."
  type        = string
  default     = null
}

variable "name_prefix" {
  description = "Lowercase resource prefix, 3-12 characters; a stable resource-group hash makes names unique."
  type        = string
  default     = "marmara-demo"

  validation {
    condition     = can(regex("^[a-z][a-z0-9-]{1,10}[a-z0-9]$", var.name_prefix)) && !strcontains(var.name_prefix, "--")
    error_message = "Use 3-12 lowercase letters, digits or single hyphens, starting with a letter and ending with a letter or digit."
  }
}

variable "resource_group_name" {
  description = "Existing resource group. Terraform does not create or manage this group."
  type        = string
  default     = "rg-marmara"
}

variable "sql_administrator_login" {
  description = "Display label for the Microsoft Entra SQL administrator."
  type        = string
  default     = "terraform-deployer"
}

variable "sql_administrator_object_id" {
  description = "Entra SQL administrator object ID. Defaults to the identity running Terraform."
  type        = string
  default     = null

  validation {
    condition     = var.sql_administrator_object_id == null || can(regex("^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$", var.sql_administrator_object_id))
    error_message = "The SQL administrator object ID must be a GUID."
  }
}

variable "sql_location" {
  description = "SQL region override for subscription offer restrictions; private endpoints remain with the app."
  type        = string
  default     = null
}

variable "sql_auto_pause_delay_in_minutes" {
  description = "Idle time before serverless SQL pauses; -1 disables auto-pause."
  type        = number
  default     = -1

  validation {
    condition     = var.sql_auto_pause_delay_in_minutes == -1 || (var.sql_auto_pause_delay_in_minutes >= 60 && var.sql_auto_pause_delay_in_minutes <= 10080)
    error_message = "Use -1 to disable auto-pause, or 60-10080 minutes."
  }
}

variable "sql_min_capacity" {
  description = "Minimum vCore capacity when the serverless database is running."
  type        = number
  default     = 0.5
}

variable "sql_sku_name" {
  description = "SQL database SKU; set explicitly to preserve the existing database's service tier."
  type        = string
  default     = "GP_S_Gen5_1"
}

variable "subscription_id" {
  description = "Azure subscription containing the existing target resource group."
  type        = string
  default     = "44d3e5d8-23bc-4517-a680-f2d6359dd516"

  validation {
    condition     = can(regex("^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$", var.subscription_id))
    error_message = "The subscription ID must be a GUID."
  }
}
