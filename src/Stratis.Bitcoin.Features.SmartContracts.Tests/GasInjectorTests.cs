﻿using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using NBitcoin;
using Stratis.SmartContracts;
using Stratis.SmartContracts.Core;
using Stratis.SmartContracts.Core.Backend;
using Stratis.SmartContracts.Core.Compilation;
using Stratis.SmartContracts.Core.State;
using Xunit;
using Block = Stratis.SmartContracts.Core.Block;

namespace Stratis.Bitcoin.Features.SmartContracts.Tests
{
    public class GasInjectorTests
    {
        private const string TestSource = @"using System;
                                            using Stratis.SmartContracts;   

                                            public class Test : SmartContract
                                            {
                                                public Test(ISmartContractState state) : base(state) {}

                                                public void TestMethod(int number)
                                                {
                                                    int test = 11 + number;
                                                    var things = new string[]{""Something"", ""SomethingElse""};
                                                    test += things.Length;
                                                }
                                            }";

        private const string TestSingleConstructorSource = @"using System;
                                            using Stratis.SmartContracts;   

                                            public class Test : SmartContract
                                            {
                                                public Test(ISmartContractState state) : base(state) 
                                                {
                                                    this.Owner = ""Test Owner"";
                                                }

                                                public void TestMethod(int number)
                                                {
                                                    int test = 11 + number;
                                                    var things = new string[]{""Something"", ""SomethingElse""};
                                                    test += things.Length;
                                                }

                                                public string Owner {
                                                    get => this.PersistentState.GetObject<string>(""Owner"");
                                                    set => this.PersistentState.SetObject<string>(""Owner"", value);
                                                }
                                            }";

        private const string TestMultipleConstructorSource = @"using System;
                                            using Stratis.SmartContracts;   

                                            public class Test : SmartContract
                                            {
                                                public Test(ISmartContractState state, string ownerName) : base(state) 
                                                {
                                                    this.Owner = ownerName;
                                                }

                                                public void TestMethod(int number)
                                                {
                                                    int test = 11 + number;
                                                    var things = new string[]{""Something"", ""SomethingElse""};
                                                    test += things.Length;
                                                }

                                                public string Owner {
                                                    get => this.PersistentState.GetObject<string>(""Owner"");
                                                    set => this.PersistentState.SetObject<string>(""Owner"", value);
                                                }
                                            }";

        private const string ContractName = "Test";
        private const string MethodName = "TestMethod";
        private static readonly Address TestAddress =  (Address) "mipcBbFg9gMiCh81Kj8tqqdgoZub1ZJRfn";

        private readonly IKeyEncodingStrategy keyEncodingStrategy = BasicKeyEncodingStrategy.Default;

        private readonly ContractStateRepositoryRoot repository = new ContractStateRepositoryRoot(new NoDeleteSource<byte[], byte[]>(new MemoryDictionarySource()));

        private readonly Network network = Network.SmartContractsRegTest;

        [Fact]
        public void TestGasInjector()
        {
            SmartContractCompilationResult compilationResult = SmartContractCompiler.Compile(TestSource);
            Assert.True(compilationResult.Success);

            byte[] originalAssemblyBytes = compilationResult.Compilation;

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(AppContext.BaseDirectory);
            int aimGasAmount;

            using (ModuleDefinition moduleDefinition = ModuleDefinition.ReadModule(
                new MemoryStream(originalAssemblyBytes),
                new ReaderParameters {AssemblyResolver = resolver}))
            {
                TypeDefinition contractType = moduleDefinition.GetType(ContractName);
                MethodDefinition testMethod = contractType.Methods.FirstOrDefault(x => x.Name == MethodName);
                aimGasAmount =
                    testMethod?.Body?.Instructions?
                        .Count ?? 10000000;
            }

            var gasLimit = (Gas) 500000;
            var gasMeter = new GasMeter(gasLimit);
            var persistenceStrategy = new MeteredPersistenceStrategy(this.repository, gasMeter, this.keyEncodingStrategy);
            var persistentState = new PersistentState(this.repository, persistenceStrategy,
                TestAddress.ToUint160(this.network), this.network);
            var vm = new ReflectionVirtualMachine(persistentState);

            var executionContext = new SmartContractExecutionContext(
                new Block(0, TestAddress),
                new Message(TestAddress, TestAddress, 0, (Gas) 500000), 1, new object[] {1});

            var internalTransactionExecutor = new InternalTransactionExecutor(this.repository, this.network, this.keyEncodingStrategy);
            ulong GetBalance() => this.repository.GetCurrentBalance(TestAddress.ToUint160(this.network));

            ISmartContractExecutionResult result = vm.ExecuteMethod(
                originalAssemblyBytes,
                MethodName,
                executionContext,
                gasMeter,
                internalTransactionExecutor,
                GetBalance);

            Assert.Equal(aimGasAmount, Convert.ToInt32(result.GasConsumed));            
        }

        [Fact]
        public void TestGasInjector_OutOfGasFails()
        {
            SmartContractCompilationResult compilationResult = SmartContractCompiler.CompileFile("SmartContracts/OutOfGasTest.cs");
            Assert.True(compilationResult.Success);

            byte[] originalAssemblyBytes = compilationResult.Compilation;

            var gasLimit = (Gas)500000;
            var gasMeter = new GasMeter(gasLimit);
            var persistenceStrategy = new MeteredPersistenceStrategy(this.repository, gasMeter, this.keyEncodingStrategy);
            var persistentState = new PersistentState(this.repository, persistenceStrategy, TestAddress.ToUint160(this.network), this.network);
            var vm = new ReflectionVirtualMachine(persistentState);

            var executionContext = new SmartContractExecutionContext(new Block(0, TestAddress), new Message(TestAddress, TestAddress, 0, (Gas)500000), 1);

            var internalTransactionExecutor = new InternalTransactionExecutor(this.repository, this.network, this.keyEncodingStrategy);
            ulong GetBalance() => this.repository.GetCurrentBalance(TestAddress.ToUint160(this.network));

            ISmartContractExecutionResult result = vm.ExecuteMethod(
                originalAssemblyBytes,
                "UseAllGas",
                executionContext,
                gasMeter,
                internalTransactionExecutor,
                GetBalance);

            Assert.NotNull(result.Exception);
            Assert.Equal((Gas)0, gasMeter.GasAvailable);
            Assert.Equal(gasLimit, result.GasConsumed);
            Assert.Equal(gasLimit, gasMeter.GasConsumed);
        }

        [Fact]
        public void SmartContracts_GasInjector_SingleParamConstructorGasInjectedSuccess()
        {
            SmartContractCompilationResult compilationResult =
                SmartContractCompiler.Compile(TestSingleConstructorSource);

            Assert.True(compilationResult.Success);
            byte[] originalAssemblyBytes = compilationResult.Compilation;

            var gasLimit = (Gas)500000;
            var gasMeter = new GasMeter(gasLimit);
            var persistenceStrategy = new MeteredPersistenceStrategy(this.repository, gasMeter, new BasicKeyEncodingStrategy());
            var persistentState = new PersistentState(this.repository, persistenceStrategy, TestAddress.ToUint160(this.network), this.network);
            var vm = new ReflectionVirtualMachine(persistentState);

            var executionContext = new SmartContractExecutionContext(new Block(0, TestAddress), new Message(TestAddress, TestAddress, 0, (Gas)500000), 1);

            var internalTransactionExecutor = new InternalTransactionExecutor(this.repository, this.network, new BasicKeyEncodingStrategy());
            ulong GetBalance() => this.repository.GetCurrentBalance(TestAddress.ToUint160(this.network));

            ISmartContractExecutionResult result = vm.Create(
                originalAssemblyBytes,                    
                executionContext,
                gasMeter,
                internalTransactionExecutor,
                GetBalance);

            // Constructor: 10
            // Property setter: 7
            // Storage: 15
            Assert.Equal((Gas) 32, result.GasConsumed);            
        }

        [Fact]
        public void SmartContracts_GasInjector_MultipleParamConstructorGasInjectedSuccess()
        {
            SmartContractCompilationResult compilationResult =
                SmartContractCompiler.Compile(TestMultipleConstructorSource);

            Assert.True(compilationResult.Success);
            byte[] originalAssemblyBytes = compilationResult.Compilation;

            var gasLimit = (Gas)500000;
            var gasMeter = new GasMeter(gasLimit);
            var persistenceStrategy = new MeteredPersistenceStrategy(this.repository, gasMeter, new BasicKeyEncodingStrategy());
            var persistentState = new PersistentState(this.repository, persistenceStrategy, TestAddress.ToUint160(this.network), this.network);
            var vm = new ReflectionVirtualMachine(persistentState);

            var executionContext = new SmartContractExecutionContext(new Block(0, TestAddress), new Message(TestAddress, TestAddress, 0, (Gas)500000), 1, new []{ "Tset Owner"});

            var internalTransactionExecutor = new InternalTransactionExecutor(this.repository, this.network, new BasicKeyEncodingStrategy());
            ulong GetBalance() => this.repository.GetCurrentBalance(TestAddress.ToUint160(this.network));

            ISmartContractExecutionResult result = vm.Create(
                originalAssemblyBytes,
                executionContext,
                gasMeter,
                internalTransactionExecutor,
                GetBalance);

            // Constructor: 10
            // Property setter: 7
            // Storage: 15
            Assert.Equal((Gas)32, result.GasConsumed);
        }
    }
}