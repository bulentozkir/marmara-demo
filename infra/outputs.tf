output "app_url" {
  description = "HTTPS URL of the deployed Todo app."
  value       = "https://${azurerm_cdn_frontdoor_endpoint.app.host_name}"
}

output "frontdoor_profile_name" {
  description = "Azure Front Door Standard profile publishing the application."
  value       = azurerm_cdn_frontdoor_profile.app.name
}

output "origin_url" {
  description = "App Service origin URL; direct requests are intentionally blocked."
  value       = "https://${azurerm_linux_web_app.app.default_hostname}"
}

output "key_vault_name" {
  description = "Private Key Vault containing the managed-identity SQL connection string."
  value       = azurerm_key_vault.app.name
}

output "managed_identity_client_id" {
  description = "Application/client ID used for the SQL contained-user SID and managed-identity connection."
  value       = azurerm_user_assigned_identity.app.client_id
}

output "managed_identity_principal_id" {
  description = "Entra object ID of the app identity, granted only SQL reader/writer permissions."
  value       = azurerm_user_assigned_identity.app.principal_id
}

output "region" {
  description = "Region of the deployed resources."
  value       = local.location
}

output "resource_group_name" {
  description = "Existing resource group, not owned by this Terraform state."
  value       = data.azurerm_resource_group.target.name
}

output "resource_group_url" {
  description = "Azure portal link to the target resource group."
  value       = "https://portal.azure.com/#@${data.azurerm_client_config.current.tenant_id}/resource${data.azurerm_resource_group.target.id}/overview"
}

output "resource_ids" {
  description = "Resource IDs managed by this Terraform configuration."
  value = [
    azurerm_cdn_frontdoor_profile.app.id,
    azurerm_cdn_frontdoor_endpoint.app.id,
    azurerm_cdn_frontdoor_origin_group.app.id,
    azurerm_cdn_frontdoor_origin.app.id,
    azurerm_cdn_frontdoor_route.app.id,
    azurerm_virtual_network.app.id,
    azurerm_subnet.app.id,
    azurerm_subnet.private_endpoints.id,
    azurerm_private_dns_zone.sql.id,
    azurerm_private_dns_zone.key_vault.id,
    azurerm_private_dns_zone_virtual_network_link.sql.id,
    azurerm_private_dns_zone_virtual_network_link.key_vault.id,
    azurerm_user_assigned_identity.app.id,
    azurerm_mssql_server.app.id,
    azurerm_mssql_database.app.id,
    azurerm_key_vault.app.id,
    azapi_resource.sql_connection.id,
    azurerm_role_assignment.key_vault_reader.id,
    azurerm_private_endpoint.sql.id,
    azurerm_private_endpoint.key_vault.id,
    azurerm_service_plan.app.id,
    azurerm_linux_web_app.app.id,
  ]
}

output "sql_database_name" {
  description = "Database initialized by the deployment script."
  value       = azurerm_mssql_database.app.name
}

output "sql_region" {
  description = "SQL hosting region, which may differ from the app because of subscription offer restrictions."
  value       = azurerm_mssql_server.app.location
}

output "sql_server_fqdn" {
  description = "SQL hostname that resolves privately from the application VNet."
  value       = azurerm_mssql_server.app.fully_qualified_domain_name
}

output "subscription_id" {
  description = "Deployment subscription."
  value       = var.subscription_id
}

output "web_app_id" {
  description = "App Service resource ID."
  value       = azurerm_linux_web_app.app.id
}

output "web_app_name" {
  description = "App Service name for Azure CLI deployment and logging."
  value       = azurerm_linux_web_app.app.name
}
