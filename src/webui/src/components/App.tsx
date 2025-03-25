import React, { useState, useEffect, useRef, useCallback, useMemo } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import ChatService, { GitHubIssue } from '../services/ChatService';
import { Message, GitHubIssueWithStatus } from '../types/ChatTypes';
import Sidebar from './Sidebar';
import ChatContainer from './ChatContainer';
import VirtualizedChatList from './VirtualizedChatList';
import './App.css';

const loadingIndicatorId = 'loading-indicator';

interface IssueParams {
    owner?: string;
    repository?: string;
    issueNumber?: string;
    [key: string]: string | undefined;
}

const App: React.FC = () => {
    const [messages, setMessages] = useState<Message[]>([]);
    const [prompt, setPrompt] = useState<string>('');
    const [issues, setIssues] = useState<GitHubIssueWithStatus[]>([]);
    const [selectedIssue, setSelectedIssue] = useState<GitHubIssue | null>(null);
    const selectedIssueRef = useRef<GitHubIssue | null>(null);
    const [loadingIssues, setLoadingIssues] = useState<boolean>(true);
    const messagesEndRef = useRef<HTMLDivElement>(null);
    const abortControllerRef = useRef<AbortController | null>(null);
    const [shouldAutoScroll, setShouldAutoScroll] = useState<boolean>(true);
    const [streamingMessageId, setStreamingMessageId] = useState<string | null>(null);
    const { owner, repository, issueNumber } = useParams<IssueParams>();
    const navigate = useNavigate();
    const POLL_INTERVAL = 5000;
    const pollIntervalRef = useRef<NodeJS.Timeout | null>(null);

    const chatService = useMemo(() => ChatService.getInstance('/api/issues'), []);

    useEffect(() => {
        const fetchIssues = async () => {
            try {
                const data = await chatService.getActiveIssues();
                setIssues(prevIssues => {
                    // Only update if the issues have actually changed
                    const hasChanged = JSON.stringify(data) !== JSON.stringify(prevIssues);
                    return hasChanged ? data : prevIssues;
                });
            } catch (error) {
                console.error('Error fetching issues:', error);
            } finally {
                setLoadingIssues(false);
            }
        };

        // Initial fetch
        fetchIssues();

        // Set up polling
        pollIntervalRef.current = setInterval(fetchIssues, POLL_INTERVAL);

        // Initial issue selection if URL parameters exist
        if (owner && repository && issueNumber) {
            const issue: GitHubIssue = {
                owner,
                repository,
                issueNumber: parseInt(issueNumber, 10)
            };
            handleIssueSelect(issue);
        }

        // Cleanup polling on unmount
        return () => {
            if (pollIntervalRef.current) {
                clearInterval(pollIntervalRef.current);
            }
        };
    }, [owner, repository, issueNumber, chatService]);

    const onSelectIssue = useCallback((issue: GitHubIssue) => {
        navigate(`/issue/${issue.owner}/${issue.repository}/${issue.issueNumber}`);
    }, [navigate]);

    const scrollToBottom = useCallback(() => {
        if (messagesEndRef.current && shouldAutoScroll) {
            messagesEndRef.current.scrollTo({
                top: messagesEndRef.current.scrollHeight,
                behavior: streamingMessageId ? 'smooth' : 'instant'
            });
        }
    }, [shouldAutoScroll, streamingMessageId]);

    const handleIssueSelect = useCallback(async (issue: GitHubIssue) => {
        setSelectedIssue(issue);
        selectedIssueRef.current = issue;
        setMessages([]);

        if (abortControllerRef.current) {
            abortControllerRef.current.abort();
        }
        const abortController = new AbortController();
        abortControllerRef.current = abortController;

        (async () => {
            try {
                console.log('streamIssue started:', issue);
                const stream = chatService.stream(issue, 0, abortController);
                
                let currentResponseId: string | null = null;
                
                for await (const fragment of stream) {
                    if (selectedIssueRef.current !== issue) {
                        break;
                    }
                    
                    if (fragment.responseId && fragment.responseId !== currentResponseId) {
                        currentResponseId = fragment.responseId;
                        if (!fragment.isFinal) {
                            setStreamingMessageId(currentResponseId);
                        }
                    }
                    
                    if (fragment.isFinal) {
                        setStreamingMessageId(null);
                        currentResponseId = null;
                        if (!fragment.text) continue;
                    }

                    const messageId = fragment.responseId;
                    updateMessageById(messageId, fragment.text, fragment.role, fragment.type, fragment.isFinal, fragment.data);
                }
            } catch (error) {
                console.error('Stream error:', error);
            } finally {
                console.log('streamIssue finished:', issue);
                setStreamingMessageId(null);
            }
        })();
    }, [chatService, scrollToBottom]);

    const updateMessageById = (id: string, newText: string, role: string, type: string, isFinal: boolean = false, data: string | undefined = undefined) => {
        const generatingReplyMessageText = 'Generating reply...';

        function getMessageText(existingText: string | undefined, newText: string): string {
            // If existingText is undefined or null, just return the new text
            if (!existingText) {
                return newText;
            }
            
            // If the existing text is the same as the generating reply message text, replace it with the new text 
            if (existingText === generatingReplyMessageText) {
                return newText;
            }

            // If the existing text starts with the generating reply message text, replace it with the new text
            if (existingText.startsWith(generatingReplyMessageText)) {
                return existingText.replace(generatingReplyMessageText, '') + newText;
            }

            if (newText.startsWith(existingText))
            {
                return newText;
            }

            // Otherwise, append the new text to the existing text
            return existingText + newText;
        }

        setMessages(prevMessages => {
            const lastUserMessage = prevMessages.filter(m => m.role === 'user').slice(-1)[0];
            if (isFinal && lastUserMessage && lastUserMessage.text === newText) {
                return prevMessages;
            }
            const existingMessage = prevMessages.find(msg => msg.responseId === id);
            if (existingMessage) {
                return prevMessages.map(msg =>
                    msg.responseId === id 
                        ? {
                            ...msg,
                            text: isFinal ? newText : getMessageText(msg.text, newText),
                            isLoading: false,
                            role: role || msg.role,
                            type: type || msg.type,
                            data: data || msg.data
                        }
                        : msg
                );
            } else {
                return [...prevMessages.filter(msg => msg.responseId !== loadingIndicatorId),
                { responseId: id, role, text: newText, type: type, isLoading: false, data: data },
                ];
            }
        });
    };

    const handleScroll = useCallback((e: React.UIEvent<HTMLDivElement>) => {
        const container = e.target as HTMLDivElement;
        const isNearBottom = container.scrollHeight - container.scrollTop - container.clientHeight < 100;
        setShouldAutoScroll(isNearBottom);
    }, []);

    useEffect(() => {
        const container = messagesEndRef.current;
        if (container) {
            container.addEventListener('scroll', handleScroll as unknown as EventListener);
            return () => container.removeEventListener('scroll', handleScroll as unknown as EventListener);
        }
    }, [handleScroll]);

    useEffect(() => {
        if (shouldAutoScroll) {
            scrollToBottom();
        }
    }, [messages, scrollToBottom]);

    const handleSubmit = useCallback(async (e: React.FormEvent) => {
        e.preventDefault();
        if (!prompt.trim() || !selectedIssue) return;
        if (streamingMessageId) return;

        const userMessage = { responseId: `${Date.now()}`, role: 'user', text: prompt } as Message;
        setMessages(prevMessages => [...prevMessages, userMessage]);

        setMessages(prevMessages => [
            ...prevMessages,
            { responseId: loadingIndicatorId, role: 'assistant', text: 'Generating reply...', type: 'AssistantResponseFragment' }
        ]);

        try {
            await chatService.sendPrompt(selectedIssue, prompt);
            setPrompt('');
        } catch (error) {
            console.error('handleSubmit error:', error);
            setMessages(prev =>
                prev.map(msg =>
                    msg.responseId === loadingIndicatorId ? { ...msg, text: '[Error in receiving response]' } : msg
                )
            );
        }
    }, [prompt, selectedIssue, streamingMessageId, chatService]);

    const handleDeleteIssueChat = async (e: React.MouseEvent, issue: GitHubIssue) => {
        e.stopPropagation();
        try {
            await chatService.deleteIssueChat(issue);
            
            // Remove the issue from the list or mark it as inactive
            setIssues(prevIssues => prevIssues.filter(i => 
                !(i.id.owner === issue.owner && 
                  i.id.repository === issue.repository && 
                  i.id.issueNumber === issue.issueNumber)
            ));
            
            if (selectedIssue && 
                selectedIssue.owner === issue.owner && 
                selectedIssue.repository === issue.repository && 
                selectedIssue.issueNumber === issue.issueNumber) {
                setSelectedIssue(null);
                setMessages([]);
            }
        } catch (error) {
            console.error('handleDeleteIssueChat error:', issue, error);
        }
    };

    const cancelChat = () => {
        if (!streamingMessageId || !selectedIssue) return;
        chatService.cancelIssueChat(selectedIssue);
    };

    // Transform GitHubIssue into Chat objects for compatibility with Sidebar
    const issuesToChats = useMemo(() => {
        return issues.map(issue => ({
            id: `${issue.id.owner}/${issue.id.repository}/${issue.id.issueNumber}`,
            issue
        }));
    }, [issues]);

    const selectedChatId = selectedIssue ? 
        `${selectedIssue.owner}/${selectedIssue.repository}/${selectedIssue.issueNumber}` : null;

    return (
        <div className="app-container">
            <Sidebar
                chats={issuesToChats}
                selectedChatId={selectedChatId}
                loadingChats={loadingIssues}
                handleDeleteChat={(e, chatId) => {
                    const chat = issuesToChats.find(c => c.id === chatId);
                    if (chat?.issue) {
                        handleDeleteIssueChat(e, chat.issue.id);
                    }
                }}
                onSelectChat={(chatId) => {
                    const chat = issuesToChats.find(c => c.id === chatId);
                    if (chat?.issue) {
                        onSelectIssue(chat.issue.id);
                    }
                }}
            />
            <ChatContainer
                messages={messages}
                prompt={prompt}
                setPrompt={setPrompt}
                handleSubmit={handleSubmit}
                cancelChat={cancelChat}
                streamingMessageId={streamingMessageId}
                messagesEndRef={messagesEndRef}
                shouldAutoScroll={shouldAutoScroll}
                chatId={selectedChatId || ''}
                renderMessages={() => (
                    <VirtualizedChatList messages={messages} />
                )}
            />
        </div>
    );
};

export default App;
