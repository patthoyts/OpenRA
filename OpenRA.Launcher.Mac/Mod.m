/*
 * Copyright 2007-2010 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made 
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see LICENSE.
 */

#import "Mod.h"


@implementation Mod
@synthesize mod;
@synthesize title;
@synthesize version;
@synthesize author;
@synthesize description;
@synthesize requires;
@synthesize standalone;
@synthesize path;

+ (id)modWithId:(NSString *)mod fields:(id)fields path:(NSString *)aPath
{
	id newObject = [[self alloc] initWithId:mod fields:fields path:aPath];
	[newObject autorelease];
	return newObject;
}

- (id)initWithId:(NSString *)anId fields:(NSDictionary *)fields path:(NSString *)aPath
{
	self = [super init];
	if (self)
	{
		mod = [anId retain];
		path = [aPath retain];
		title = [[fields objectForKey:@"Title"] retain];
		version = [[fields objectForKey:@"Version"] retain];
		author = [[fields objectForKey:@"Author"] retain];
		description = [[fields objectForKey:@"Description"] retain];
		requires = [[fields objectForKey:@"Requires"] retain];
		standalone = ([[fields objectForKey:@"Standalone"] isEqualToString:@"True"]);
	}
	return self;
}

- (void) dealloc
{
	[mod release]; mod = nil;
	[path release]; path = nil;
	[title release]; title = nil;
	[version release]; version = nil;
	[author release]; author = nil;
	[description release]; description = nil;	
	[requires release]; requires = nil;	
	[super dealloc];
}

@end
