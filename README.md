# marmara-demo

"# 1. Clone the full repository URL and enter the todo_app folder

git clone https://github.com
cd azure-sql-db-aspire/todo_app

# 2. Authenticate with your Azure for Students account

azd auth login

# 3. Enable cloud builds so Azure builds the Docker images instead of your PC

azd config set alpha.aca.remoteBuild on

# 4. Initialize the Aspire environment and deploy to Azure Container Apps + Azure SQL

azd init

azd up
"