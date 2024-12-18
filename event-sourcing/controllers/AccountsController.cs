﻿using event_sourcing.aggregateroot;
using event_sourcing.events;
using event_sourcing.repositories.contracts;
using event_sourcing.requests;
using Microsoft.AspNetCore.Mvc;

namespace event_sourcing.controllers;

[ApiController]
[Route("[controller]")]
public class AccountsController : ControllerBase
{
    private readonly IEventStoreRepository _eventStoreRepository;
    private readonly ISnapshotStoreRepository _snapshotStoreRepository;

    public AccountsController(IEventStoreRepository eventStoreRepository,
        ISnapshotStoreRepository snapshotStoreRepository)
    {
        _eventStoreRepository = eventStoreRepository;
        _snapshotStoreRepository = snapshotStoreRepository;
    }

    [HttpPost]
    public async Task<ActionResult> CreateAccount([FromBody] decimal initialBalance)
    {
        var accountId = Guid.NewGuid();
        var accountCreatedEvent = new AccountCreatedDomainEvent(accountId, initialBalance);

        await _eventStoreRepository.SaveEvents(accountId, [accountCreatedEvent]);

        return Ok(new { AccountId = accountId });
    }

    [HttpPost("deposit")]
    public async Task<ActionResult> Deposit([FromBody] DepositRequest depositRequest)
    {
        var events = await _eventStoreRepository.GetEventsForAggregate(depositRequest.AccountId);
        var account = BankAccount.Rehydrate(depositRequest.AccountId,events);

        account.Deposit(depositRequest.Amount);

        await _eventStoreRepository.SaveEvents(depositRequest.AccountId, account.GetUncommittedDomainEvents());
        await _snapshotStoreRepository.SaveSnapshot(depositRequest.AccountId, account.Balance);

        return Ok(new { Balance = account.Balance });
    }

    [HttpPost("withdraw")]
    public async Task<ActionResult> Withdraw([FromBody] WithdrawRequest withdrawRequest)
    {
        var events = await _eventStoreRepository.GetEventsForAggregate(withdrawRequest.AccountId);

        var account = BankAccount.Rehydrate(withdrawRequest.AccountId,events);
        account.Withdraw(withdrawRequest.Amount);

        await _eventStoreRepository.SaveEvents(withdrawRequest.AccountId, account.GetUncommittedDomainEvents());
        await _snapshotStoreRepository.SaveSnapshot(withdrawRequest.AccountId, account.Balance);

        return Ok(new { Balance = account.Balance });
    }

    [HttpGet("{accountId}")]
    public async Task<ActionResult> GetAccount(Guid accountId)
    {
        var events = await _eventStoreRepository.GetEventsForAggregate(accountId);
        var account = BankAccount.Rehydrate(accountId, events);
        
        return Ok(new { Balance = account.Balance });
    }
}