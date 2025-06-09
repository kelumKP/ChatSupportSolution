# ChatSupport Solution

This repository contains a modular .NET 9.0 solution for managing chat support operations. The solution is organized into the following projects:

## Projects

- **ChatSupport.API**  
  ASP.NET Core Web API project that exposes endpoints for chat support operations.

- **ChatSupport.Application**  
  Contains application logic and services for handling chat support workflows.

- **ChatSupport.Domain**  
  Defines core domain models such as `Agent`, `ChatSession`, `SupportTeam`, and `Seniority`.

- **ChatSupport.Tests**  
  Unit tests for application and domain logic, using xUnit.

## Getting Started

1. **Build the solution**  
   Open the solution in Visual Studio or run:
   ```sh
   dotnet build ChatSupport.sln
   ```

2. **Run the API**  
   Navigate to the `ChatSupport.API` directory and run:
   ```sh
   dotnet run
   ```

3. **Run tests**  
   Navigate to the `ChatSupport.Tests` directory and run:
   ```sh
   dotnet test
   ```

## Requirements

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Project Structure

- `ChatSupport.API/` - API project
- `ChatSupport.Application/` - Application services
- `ChatSupport.Domain/` - Domain models
- `ChatSupport.Tests/` - Unit tests

---

