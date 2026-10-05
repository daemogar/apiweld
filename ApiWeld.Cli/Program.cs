using ApiWeld.Cli;

return args is ["generate", ..]
	? GenerateCommand.Run(args, Console.Out, Console.Error)
	: Normalizer.Run(args, Console.Out, Console.Error);
