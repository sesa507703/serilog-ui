﻿using Dapper;
using MySql.Data.MySqlClient;
using Serilog.Ui.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Serilog.Ui.MySqlProvider
{
    public class MySqlDataProvider : IDataProvider
    {
        private readonly RelationalDbOptions _options;

        public MySqlDataProvider(RelationalDbOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<(IEnumerable<LogModel>, int)> FetchDataAsync(
            int page,
            int count,
            string logLevel = null,
            string searchCriteria = null,
            DateTime? startDate = null,
            DateTime? endDate = null
        )
        {
            var logsTask = GetLogsAsync(page - 1, count, logLevel, searchCriteria, startDate, endDate);
            var logCountTask = CountLogsAsync(logLevel, searchCriteria, startDate, endDate);

            await Task.WhenAll(logsTask, logCountTask);

            return (await logsTask, await logCountTask);
        }

        private async Task<IEnumerable<LogModel>> GetLogsAsync(
            int page,
            int count,
            string level,
            string searchCriteria,
            DateTime? startDate,
            DateTime? endDate)
        {
            var queryBuilder = new StringBuilder();
            queryBuilder.Append("SELECT Id, Message, Level, TimeStamp, Exception, Properties From `");
            queryBuilder.Append(_options.TableName);
            queryBuilder.Append("` ");

            GenerateWhereClause(queryBuilder, level, searchCriteria, startDate, endDate);

            queryBuilder.Append("ORDER BY Id DESC LIMIT @Offset, @Count");

            using (var connection = new MySqlConnection(_options.ConnectionString))
            {
                var param = new
                {
                    Offset = page * count,
                    Count = count,
                    Level = level,
                    Search = searchCriteria != null ? $"%{searchCriteria}%" : null,
                    StartDate = startDate,
                    EndDate = endDate
                };
                var logs = await connection.QueryAsync<MySqlLogModel>(queryBuilder.ToString(), param);
                var index = 1;
                foreach (var log in logs)
                    log.RowNo = (page * count) + index++;

                return logs;
            }
        }

        private async Task<int> CountLogsAsync(
            string level,
            string searchCriteria,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            var queryBuilder = new StringBuilder();
            queryBuilder.Append("SELECT COUNT(Id) FROM `");
            queryBuilder.Append(_options.TableName);
            queryBuilder.Append("` ");

            GenerateWhereClause(queryBuilder, level, searchCriteria, startDate, endDate);

            using (var connection = new MySqlConnection(_options.ConnectionString))
            {
                return await connection.ExecuteScalarAsync<int>(queryBuilder.ToString(),
                    new
                    {
                        Level = level,
                        Search = searchCriteria != null ? "%" + searchCriteria + "%" : null,
                        StartDate = startDate,
                        EndDate = endDate
                    });
            }
        }

        private void GenerateWhereClause(
            StringBuilder queryBuilder,
            string level,
            string searchCriteria,
            DateTime? startDate = null,
            DateTime? endDate = null)
        {
            var whereIncluded = false;

            if (!string.IsNullOrEmpty(level))
            {
                queryBuilder.Append("WHERE LogLevel = @Level ");
                whereIncluded = true;
            }

            if (!string.IsNullOrEmpty(searchCriteria))
            {
                queryBuilder.Append(whereIncluded
                    ? "AND (Message LIKE @Search OR Exception LIKE @Search) "
                    : "WHERE (Message LIKE @Search OR Exception LIKE @Search) ");
                whereIncluded = true;
            }

            if (startDate != null)
            {
                queryBuilder.Append(whereIncluded
                    ? "AND TimeStamp >= @StartDate "
                    : "WHERE TimeStamp >= @StartDate ");
                whereIncluded = true;
            }

            if (endDate != null)
            {
                queryBuilder.Append(whereIncluded
                    ? "AND TimeStamp <= @EndDate "
                    : "WHERE TimeStamp <= @EndDate ");
            }
        }
    }
}
